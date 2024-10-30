using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

public class TinyRenderer
{
    private static readonly ProfilingSampler s_GizmosPass = new ("Gizmos Pass");
    private static readonly ProfilingSampler s_SetupCamera = new ("Setup Camera Properties");

    private RTHandle m_TargetColorHandle;
    private RTHandle m_TargetDepthHandle;
    private TextureHandle m_BackbufferColorTexture;
    private TextureHandle m_BackbufferDepthTexture;

    private static RTHandle[] m_CameraColorHandles = { null, null };
    private static RTHandle m_CameraDepthHandle;
    private static TextureHandle[] m_RenderGraphCameraColorTextures = { TextureHandle.nullHandle, TextureHandle.nullHandle };
    private static TextureHandle m_RenderGraphCameraDepthTexture;
    private static int m_CurrentColorTexture = 0;
    private TextureHandle currentCameraColorTexture => m_RenderGraphCameraColorTextures[m_CurrentColorTexture];

    private TextureHandle nextCameraColorTexture
    {
        get
        {
            m_CurrentColorTexture = (m_CurrentColorTexture + 1) % 2;
            return m_RenderGraphCameraColorTextures[m_CurrentColorTexture];
        }
    }

    private TextureHandle m_ActiveCameraColorTexture;
    private TextureHandle m_ActiveCameraDepthTexture;

    private TextureHandle m_MainLightShadowMapTexture;
    private TextureHandle m_AdditionalLightShadowMapTexture;

    private TextureHandle m_DepthTexture;
    private TextureHandle m_CameraColorTexture;

    private TextureHandle m_ColorGradingLutTexture;

    private bool m_IsActiveTargetBackbuffer;

    // Materials
    private Material m_BlitMaterial;
    private Material m_CopyDepthMaterial;

    // Lights data
    private ForwardLights m_ForwardLights;

    // Passes
    private MainLightShadowPass m_MainLightShadowPass;
    private AdditionalLightsShadowPass m_AdditionalLightsShadowPass;

    private DepthPrepass m_DepthPrepass;

    private ColorGradingLutPass m_ColorGradingLutPass;

    private DrawObjectsForwardPass m_ForwardOpaqueObjectsPass;
    private CopyDepthPass m_CopyDepthPass;
    private DrawSkyboxPass m_DrawSkyboxPass;
    private CopyColorPass m_CopyColorPass;
    private DrawObjectsForwardPass m_ForwardTransparentObjectsPass;

    private PostProcessingPass m_PostProcessingPass;
    private FXAAPass m_FXAAPass;
    private FinalBlitPass m_FinalBlitPass;

#if UNITY_EDITOR
    private CopyDepthPass m_FinalCopyDepthPass;
#endif

    public TinyRenderer()
    {
        m_BlitMaterial = CoreUtils.CreateEngineMaterial("Hidden/Tiny Render Pipeline/Blit");
        m_CopyDepthMaterial = CoreUtils.CreateEngineMaterial("Hidden/Tiny Render Pipeline/CopyDepth");

        m_ForwardLights = new ForwardLights();

        m_MainLightShadowPass = new MainLightShadowPass();
        m_AdditionalLightsShadowPass = new AdditionalLightsShadowPass();

        m_DepthPrepass = new DepthPrepass();

        m_ColorGradingLutPass = new ColorGradingLutPass();

        m_ForwardOpaqueObjectsPass = new DrawObjectsForwardPass(true);
        m_CopyDepthPass = new CopyDepthPass(m_CopyDepthMaterial);
        m_DrawSkyboxPass = new DrawSkyboxPass();
        m_CopyColorPass = new CopyColorPass(m_BlitMaterial);
        m_ForwardTransparentObjectsPass = new DrawObjectsForwardPass();

        m_PostProcessingPass = new PostProcessingPass();
        m_FXAAPass = new FXAAPass();
        m_FinalBlitPass = new FinalBlitPass(m_BlitMaterial);

#if UNITY_EDITOR
        m_FinalCopyDepthPass = new CopyDepthPass(m_CopyDepthMaterial, true);
#endif

        m_IsActiveTargetBackbuffer = true;
    }

    public void Dispose()
    {
        m_CameraColorHandles[0]?.Release();
        m_CameraColorHandles[1]?.Release();
        m_CameraDepthHandle?.Release();

        m_ForwardLights?.Cleanup();

        m_MainLightShadowPass?.Dispose();
        m_AdditionalLightsShadowPass?.Dispose();

        m_ColorGradingLutPass?.Dispose();

        m_PostProcessingPass?.Dispose();
        m_FXAAPass?.Dispose();

        CoreUtils.Destroy(m_BlitMaterial);
        CoreUtils.Destroy(m_CopyDepthMaterial);
    }

    public void RecordAndExecuteRenderGraph(RenderGraph renderGraph, ref RenderingData renderingData, string name)
    {
        var context = renderingData.renderContext;
        var renderGraphParameters = new RenderGraphParameters()
        {
            executionName = name,
            commandBuffer = renderingData.commandBuffer,
            scriptableRenderContext = context,
            currentFrameIndex = Time.frameCount
        };

        var executor = renderGraph.RecordAndExecute(renderGraphParameters);
        RecordRenderGraph(renderGraph, ref renderingData);
        executor.Dispose();
    }

    private void RecordRenderGraph(RenderGraph renderGraph, ref RenderingData renderingData)
    {
        var camera = renderingData.camera;
        var cameraType = camera.cameraType;
        bool supportIntermediateRendering = cameraType <= CameraType.SceneView;

        // Setup lights data
        m_ForwardLights.SetupRenderGraphLights(renderGraph, ref renderingData);

        // Render main light shadow map
        if (m_MainLightShadowPass.Setup(ref renderingData))
            m_MainLightShadowMapTexture = m_MainLightShadowPass.Record(renderGraph, ref renderingData);

        // Render additional lights shadow map
        if (m_AdditionalLightsShadowPass.Setup(ref renderingData))
            m_AdditionalLightShadowMapTexture = m_AdditionalLightsShadowPass.Record(renderGraph, ref renderingData);

        CreateAndSetCameraRenderTargets(renderGraph, ref renderingData, supportIntermediateRendering);

        // Setup camera properties
        SetupRenderGraphCameraProperties(renderGraph, ref renderingData, m_ActiveCameraColorTexture);

        // Depth prepass
        m_DepthPrepass.Record(renderGraph, ref m_ActiveCameraDepthTexture, ref renderingData);

        var postProcessingData = renderingData.postProcessingData;
        bool applyPostProcessing = postProcessingData && CoreUtils.ArePostProcessesEnabled(camera);
        applyPostProcessing &= supportIntermediateRendering;

        // Generate color grading LUT pass
        bool generateColorGradingLut = applyPostProcessing && renderingData.isHdrEnabled;
        if (generateColorGradingLut)
        {
            m_ColorGradingLutPass.Record(renderGraph, out m_ColorGradingLutTexture, ref renderingData);
        }

        // Copy depth pass
        if (supportIntermediateRendering)
        {
            var depthDescriptor = renderingData.cameraTargetDescriptor;
            depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
            depthDescriptor.depthStencilFormat = GraphicsFormat.None;
            depthDescriptor.depthBufferBits = (int)DepthBits.None;
            m_DepthTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, depthDescriptor, "_CameraDepthTexture", true);
            m_CopyDepthPass.Record(renderGraph, m_ActiveCameraDepthTexture, m_DepthTexture, TextureHandle.nullHandle, ref renderingData, true);
        }
        else
        {
            RenderingUtils.SetGlobalRenderGraphTextureName(renderGraph, "_CameraDepthTexture", SystemInfo.usesReversedZBuffer ? renderGraph.defaultResources.blackTexture : renderGraph.defaultResources.whiteTexture, "SetDefaultGlobalCameraDepthTexture");
        }

        // Draw opaque objects pass
        m_ForwardOpaqueObjectsPass.Record(renderGraph, m_ActiveCameraColorTexture, m_ActiveCameraDepthTexture, m_MainLightShadowMapTexture, m_AdditionalLightShadowMapTexture, ref renderingData);

        // Draw skybox pass
        m_DrawSkyboxPass.DrawRenderGraphSkybox(renderGraph, m_ActiveCameraColorTexture, m_ActiveCameraDepthTexture, ref renderingData);

        // // Copy color pass
        // if (supportIntermediateRendering)
        // {
        //     var colorDescriptor = renderingData.cameraTargetDescriptor;
        //     colorDescriptor.depthBufferBits = (int)DepthBits.None;
        //     colorDescriptor.depthStencilFormat = GraphicsFormat.None;
        //     m_CameraColorTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, colorDescriptor, "_CameraOpaqueTexture", true, FilterMode.Bilinear);
        //     m_CopyColorPass.Record(renderGraph, m_ActiveCameraColorTexture, m_CameraColorTexture, ref renderingData);
        // }
        // else
        // {
        //     RenderingUtils.SetGlobalRenderGraphTextureName(renderGraph, "_CameraOpaqueTexture", renderGraph.defaultResources.whiteTexture);
        // }
        // Set default camera color texture
        RenderingUtils.SetGlobalRenderGraphTextureName(renderGraph, "_CameraOpaqueTexture", renderGraph.defaultResources.whiteTexture);

        // Draw transparent objects pass
        m_ForwardTransparentObjectsPass.Record(renderGraph, m_ActiveCameraColorTexture, m_ActiveCameraDepthTexture, TextureHandle.nullHandle, TextureHandle.nullHandle, ref renderingData);

        DrawRenderGraphGizmos(renderGraph, m_ActiveCameraColorTexture, m_ActiveCameraDepthTexture, GizmoSubset.PreImageEffects, ref renderingData);

        bool hasFxaaPass = applyPostProcessing && (postProcessingData.antialiasingMode == PostProcessingData.AntialiasingMode.FastApproximateAntialiasing);

        if (applyPostProcessing)
        {
            var target = !hasFxaaPass ? m_BackbufferColorTexture : nextCameraColorTexture;
            m_PostProcessingPass.Record(renderGraph, in m_ActiveCameraColorTexture, m_ColorGradingLutTexture, target, ref renderingData);

            m_ActiveCameraColorTexture = target;
            if (!hasFxaaPass)
            {
                m_IsActiveTargetBackbuffer = true;
            }
        }

        if (hasFxaaPass)
        {
            m_FXAAPass.Record(renderGraph, m_ActiveCameraColorTexture, m_BackbufferColorTexture, ref renderingData);
            m_ActiveCameraColorTexture = m_BackbufferColorTexture;
            m_IsActiveTargetBackbuffer = true;
        }

        if (supportIntermediateRendering && !m_IsActiveTargetBackbuffer)
        {
            m_FinalBlitPass.Record(renderGraph, m_ActiveCameraColorTexture, m_BackbufferColorTexture, ref renderingData);
            m_ActiveCameraColorTexture = m_BackbufferColorTexture;
            m_IsActiveTargetBackbuffer = true;
        }

#if UNITY_EDITOR
        bool isGizmosEnabled = Handles.ShouldRenderGizmos();
        bool isSceneViewOrPreviewCamera = cameraType == CameraType.SceneView || cameraType == CameraType.Preview;
        if (supportIntermediateRendering && (isSceneViewOrPreviewCamera || isGizmosEnabled))
        {
            m_FinalCopyDepthPass.Record(renderGraph, m_ActiveCameraDepthTexture, m_BackbufferDepthTexture, m_ActiveCameraColorTexture, ref renderingData, false, "FinalDepthCopy");
        }
#endif

        // Drawing Gizmos
        DrawRenderGraphGizmos(renderGraph, m_ActiveCameraColorTexture, m_BackbufferDepthTexture, GizmoSubset.PostImageEffects, ref renderingData);
    }

    private void CreateAndSetCameraRenderTargets(RenderGraph renderGraph, ref RenderingData renderingData, bool supportIntermediateRendering)
    {
        var camera = renderingData.camera;

        bool isBuiltInTexture = (camera.targetTexture == null);
        RenderTargetIdentifier targetColorId = isBuiltInTexture ? BuiltinRenderTextureType.CameraTarget : new RenderTargetIdentifier(camera.targetTexture);
        RenderTargetIdentifier targetDepthId = isBuiltInTexture ? BuiltinRenderTextureType.Depth : new RenderTargetIdentifier(camera.targetTexture);

        if (m_TargetColorHandle == null)
            m_TargetColorHandle = RTHandles.Alloc(targetColorId, "Backbuffer color");
        else if (m_TargetColorHandle.nameID != targetColorId)
            RTHandleStaticHelpers.SetRTHandleUserManagedWrapper(ref m_TargetColorHandle, targetColorId);

        if (m_TargetDepthHandle == null)
            m_TargetDepthHandle = RTHandles.Alloc(targetDepthId, "Backbuffer depth");
        else if (m_TargetDepthHandle.nameID != targetDepthId)
            RTHandleStaticHelpers.SetRTHandleUserManagedWrapper(ref m_TargetDepthHandle, targetDepthId);

        RenderTargetInfo importInfo = new RenderTargetInfo();
        RenderTargetInfo importInfoDepth;
        if (isBuiltInTexture)
        {
            importInfo.width = Screen.width;
            importInfo.height = Screen.height;
            importInfo.volumeDepth = 1;
            importInfo.msaaSamples = 1;
            importInfo.format = renderingData.defaultFormat;
            importInfoDepth = importInfo;
            importInfoDepth.format = SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil);
        }
        else
        {
            var targetTexture = camera.targetTexture;
            importInfo.width = targetTexture.width;
            importInfo.height = targetTexture.height;
            importInfo.volumeDepth = targetTexture.volumeDepth;
            importInfo.msaaSamples = 1;
            importInfo.format = targetTexture.graphicsFormat;

            importInfoDepth = importInfo;
            importInfoDepth.format = targetTexture.depthStencilFormat;
        }

        CameraClearFlags clearFlags = camera.clearFlags;

        bool clearColor = clearFlags <= CameraClearFlags.Color;
        bool clearDepth = clearFlags <= CameraClearFlags.Depth;
        var backgroundColor = camera.backgroundColor;
        ImportResourceParams importBackbufferColorParams = new ImportResourceParams();
        importBackbufferColorParams.clearOnFirstUse = clearColor;
        importBackbufferColorParams.clearColor = backgroundColor.linear;
        importBackbufferColorParams.discardOnLastUse = false;
        ImportResourceParams importBackbufferDepthParams = new ImportResourceParams();
        importBackbufferDepthParams.clearOnFirstUse = clearDepth;
        importBackbufferDepthParams.clearColor = backgroundColor.linear;
        importBackbufferDepthParams.discardOnLastUse = false;

        m_BackbufferColorTexture = renderGraph.ImportTexture(m_TargetColorHandle, importInfo, importBackbufferColorParams);
        m_BackbufferDepthTexture = renderGraph.ImportTexture(m_TargetDepthHandle, importInfoDepth, importBackbufferDepthParams);

        var cameraTargetDescriptor = renderingData.cameraTargetDescriptor;
        cameraTargetDescriptor.depthBufferBits = (int)DepthBits.None;

        RenderingUtils.ReAllocateIfNeeded(ref m_CameraColorHandles[0], cameraTargetDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_CameraTargetAttachmentA");
        RenderingUtils.ReAllocateIfNeeded(ref m_CameraColorHandles[1], cameraTargetDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_CameraTargetAttachmentB");

        ImportResourceParams importColorParams = new ImportResourceParams();
        // Always clear on first use for intermediate render texture
        importColorParams.clearOnFirstUse = true;
        importColorParams.clearColor = backgroundColor.linear;
        importColorParams.discardOnLastUse = true;

        m_CurrentColorTexture = 0;

        m_RenderGraphCameraColorTextures[0] = renderGraph.ImportTexture(m_CameraColorHandles[0], importColorParams);
        m_RenderGraphCameraColorTextures[1] = renderGraph.ImportTexture(m_CameraColorHandles[1], importColorParams);

        var depthDescriptor = renderingData.cameraTargetDescriptor;
        depthDescriptor.graphicsFormat = GraphicsFormat.None;
        depthDescriptor.depthStencilFormat = GraphicsFormat.D32_SFloat_S8_UInt;
        depthDescriptor.depthBufferBits = 32;

        RenderingUtils.ReAllocateIfNeeded(ref m_CameraDepthHandle, depthDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_CameraDepthAttachment");

        ImportResourceParams importDepthParams = new ImportResourceParams();
        // Always clear on first use for intermediate render texture
        importDepthParams.clearOnFirstUse = true;
        importDepthParams.clearColor = backgroundColor.linear;
        importDepthParams.discardOnLastUse = true;
        m_RenderGraphCameraDepthTexture = renderGraph.ImportTexture(m_CameraDepthHandle, importDepthParams);

        if (supportIntermediateRendering)
        {
            m_ActiveCameraColorTexture = currentCameraColorTexture;
            m_ActiveCameraDepthTexture = m_RenderGraphCameraDepthTexture;
            m_IsActiveTargetBackbuffer = false;
        }
        else
        {
            m_ActiveCameraColorTexture = m_BackbufferColorTexture;
            m_ActiveCameraDepthTexture = m_BackbufferDepthTexture;
        }
    }

    private class PassData
    {
        public TextureHandle colorTarget;
        public RenderingData renderingData;
    }

    private void SetupRenderGraphCameraProperties(RenderGraph renderGraph, ref RenderingData renderingData, TextureHandle colorTarget)
    {
        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_SetupCamera.name, out var passData, s_SetupCamera))
        {
            passData.colorTarget = colorTarget;
            passData.renderingData = renderingData;

            // Not allow pass culling
            builder.AllowPassCulling(false);
            // Enable this to set global state
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext rasterGraphContext) =>
            {
                var cmd = rasterGraphContext.cmd;

                cmd.SetupCameraProperties(data.renderingData.camera);

                // This is still required because of the following reasons:
                // - Camera billboard properties.
                // - Camera frustum planes: unity_CameraWorldClipPlanes[6]
                // - _ProjectionParams.x logic is deep inside GfxDevice
                SetPerCameraShaderVariables(cmd, data.renderingData.camera, RenderingUtils.IsHandleYFlipped(data.colorTarget, data.renderingData.camera));
            });
        }
    }

    private static void SetPerCameraShaderVariables(RasterCommandBuffer cmd, Camera camera, bool isTargetFlipped)
    {
        float near = camera.nearClipPlane;
        float far = camera.farClipPlane;
        float invNear = Mathf.Approximately(near, 0.0f) ? 0.0f : 1.0f / near;
        float invFar = Mathf.Approximately(far, 0.0f) ? 0.0f : 1.0f / far;
        float isOrthographic = camera.orthographic ? 1.0f : 0.0f;
        float cameraWidth = (float)camera.pixelWidth;
        float cameraHeight = (float)camera.pixelHeight;

        // From http://www.humus.name/temp/Linearize%20depth.txt
        // But as depth component textures on OpenGL always return in 0..1 range (as in D3D), we have to use
        // the same constants for both D3D and OpenGL here.
        // OpenGL would be this:
        // zc0 = (1.0 - far / near) / 2.0;
        // zc1 = (1.0 + far / near) / 2.0;
        // D3D is this:
        float zc0 = 1.0f - far * invNear;
        float zc1 = far * invNear;

        Vector4 zBufferParams = new Vector4(zc0, zc1, zc0 * invFar, zc1 * invFar);
        if (SystemInfo.usesReversedZBuffer)
        {
            zBufferParams.y += zBufferParams.x;
            zBufferParams.x = -zBufferParams.x;
            zBufferParams.w += zBufferParams.z;
            zBufferParams.z = -zBufferParams.z;
        }

        cmd.SetGlobalVector(ShaderPropertyId.worldSpaceCameraPos, camera.transform.position);
        cmd.SetGlobalVector(ShaderPropertyId.zBufferParams, zBufferParams);
        float aspectRatio = (float)camera.pixelWidth / (float)camera.pixelHeight;
        float orthographicSize = camera.orthographicSize;
        Vector4 orthoParams = new Vector4(orthographicSize * aspectRatio, orthographicSize, 0.0f, isOrthographic);
        cmd.SetGlobalVector(ShaderPropertyId.orthoParams, orthoParams);
        float projectionFlipSign = isTargetFlipped ? -1.0f : 1.0f;
        cmd.SetGlobalVector(ShaderPropertyId.projectionParams, new Vector4(projectionFlipSign, near, far, 1.0f * invFar));
        cmd.SetGlobalVector(ShaderPropertyId.screenParams, new Vector4(cameraWidth, cameraHeight, 1.0f + 1.0f / cameraWidth, 1.0f + 1.0f / cameraHeight));
    }

#if UNITY_EDITOR
    private class DrawGizmosPassData
    {
        public RendererListHandle rendererList;
    }
#endif

    [Conditional("UNITY_EDITOR")]
    private void DrawRenderGraphGizmos(RenderGraph renderGraph, TextureHandle color, TextureHandle depth, GizmoSubset gizmoSubset, ref RenderingData renderingData)
    {
#if UNITY_EDITOR
        if (!Handles.ShouldRenderGizmos())
            return;

        using (var builder = renderGraph.AddRasterRenderPass<DrawGizmosPassData>(s_GizmosPass.name, out var passData, s_GizmosPass))
        {
            passData.rendererList = renderGraph.CreateGizmoRendererList(renderingData.camera, gizmoSubset);

            builder.UseTextureFragment(color, 0, IBaseRenderGraphBuilder.AccessFlags.Write);
            builder.UseTextureFragmentDepth(depth, IBaseRenderGraphBuilder.AccessFlags.Read);
            builder.UseRendererList(passData.rendererList);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc((DrawGizmosPassData data, RasterGraphContext rasterGraphContext) =>
            {
                rasterGraphContext.cmd.DrawRendererList(data.rendererList);
            });
        }
#endif
    }
}
