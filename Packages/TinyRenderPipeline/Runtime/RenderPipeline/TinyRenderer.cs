using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class TinyRenderer
{
    private static readonly ProfilingSampler s_GizmosPass = new ProfilingSampler("Gizmos Pass");
    private static readonly ProfilingSampler s_SetupCamera = new ProfilingSampler("Setup Camera Properties");

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

    private TextureHandle m_ScalableAOTexture;

    private bool m_IsActiveTargetBackbuffer;

    // Materials
    private Material m_BlitMaterial;
    private Material m_CopyDepthMaterial;

    // Lights data
    private ForwardLights m_ForwardLights;

    // Passes
    private MainLightShadow m_MainLightShadow;
    private AdditionalLightsShadow m_AdditionalLightsShadow;

    private DepthPrepass m_DepthPrepass;

    private ColorGradingLut m_ColorGradingLut;

    private ScalableAO m_ScalableAO;

    private DrawObjectsForward m_ForwardOpaque;
    private CopyDepth m_CopyDepth;
    private DrawSkybox m_DrawSkybox;

    private ScreenSpaceReflection m_ScreenSpaceReflection;

    private CopyColor m_CopyColor;
    private DrawObjectsForward m_ForwardTransparent;

    private PostProcess m_PostProcess;
    private FinalBlit m_FinalBlit;

    // Anti-aliasing
    private FastApproximateAA m_FastApproximateAA;

    private TemporalAA m_TemporalAA;

#if UNITY_EDITOR
    private CopyDepth m_FinalCopyDepthToCameraTarget;
#endif

    private readonly TinyRenderPipelineAsset m_PipelineAsset;

    public TinyRenderer(TinyRenderPipelineAsset asset)
    {
        m_PipelineAsset = asset;

        m_BlitMaterial = CoreUtils.CreateEngineMaterial("Hidden/Tiny Render Pipeline/Blit");
        m_CopyDepthMaterial = CoreUtils.CreateEngineMaterial("Hidden/Tiny Render Pipeline/CopyDepth");

        m_ForwardLights = new ForwardLights();

        m_MainLightShadow = new MainLightShadow();
        m_AdditionalLightsShadow = new AdditionalLightsShadow();

        m_DepthPrepass = new DepthPrepass();

        m_ColorGradingLut = new ColorGradingLut();

        m_ScalableAO = new ScalableAO(asset.shaderResources.depthPyramidCS);

        m_ForwardOpaque = new DrawObjectsForward(true);
        m_CopyDepth = new CopyDepth(m_CopyDepthMaterial, asset.shaderResources.copyDepthCS);
        m_DrawSkybox = new DrawSkybox();

        m_ScreenSpaceReflection = new ScreenSpaceReflection(asset.shaderResources.screenSpaceReflectionCS, asset.shaderResources.depthPyramidCS);

        m_CopyColor = new CopyColor(asset.shaderResources.copyColorCS);
        m_ForwardTransparent = new DrawObjectsForward();

        m_PostProcess = new PostProcess();
        m_FinalBlit = new FinalBlit(m_BlitMaterial);

        m_FastApproximateAA = new FastApproximateAA(m_PipelineAsset.shaderResources.fxaaShader);
        m_TemporalAA = new TemporalAA(asset.shaderResources.taaShader);

#if UNITY_EDITOR
        m_FinalCopyDepthToCameraTarget = new CopyDepth(m_CopyDepthMaterial, asset.shaderResources.copyDepthCS, true);
#endif

        m_IsActiveTargetBackbuffer = true;
    }

    public void Dispose()
    {
        m_CameraColorHandles[0]?.Release();
        m_CameraColorHandles[1]?.Release();
        m_CameraDepthHandle?.Release();

        m_ForwardLights?.Cleanup();

        m_MainLightShadow?.Dispose();
        m_AdditionalLightsShadow?.Dispose();

        m_ColorGradingLut?.Dispose();

        m_ScalableAO?.Dispose();

        m_PostProcess?.Dispose();

        m_TemporalAA?.Dispose();
        m_FastApproximateAA?.Dispose();

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
        var camera = renderingData.cameraData.camera;
        var cameraType = camera.cameraType;
        bool supportIntermediateRendering = cameraType <= CameraType.SceneView;

        // Setup lights data
        m_ForwardLights.SetupRenderGraphLights(renderGraph, ref renderingData);

        // Main light shadow
        if (m_MainLightShadow.IsEnabled(renderGraph, ref renderingData))
            m_MainLightShadowMapTexture = m_MainLightShadow.RecordRenderGraph(renderGraph, ref renderingData);

        // Render additional lights shadow map
        if (m_AdditionalLightsShadow.IsEnabled(renderGraph, ref renderingData))
            m_AdditionalLightShadowMapTexture = m_AdditionalLightsShadow.RecordRenderGraph(renderGraph, ref renderingData);

        CreateAndSetCameraRenderTargets(renderGraph, ref renderingData, supportIntermediateRendering);

        // Setup camera properties
        SetupRenderGraphCameraProperties(renderGraph, ref renderingData, m_ActiveCameraColorTexture);

        // Depth prepass
        m_DepthPrepass.RecordRenderGraph(renderGraph, ref m_ActiveCameraDepthTexture, ref renderingData);

        var postProcessingData = renderingData.postProcessingData;
        bool applyPostProcessing = postProcessingData && CoreUtils.ArePostProcessesEnabled(camera);
        applyPostProcessing &= supportIntermediateRendering;

        // Generate color grading LUT pass
        bool generateColorGradingLut = applyPostProcessing;
        if (generateColorGradingLut)
        {
            m_ColorGradingLut.RecordRenderGraph(renderGraph, out m_ColorGradingLutTexture, ref renderingData);
        }

        // Copy depth pass
        if (supportIntermediateRendering)
        {
            m_CopyDepth.RecordRenderGraphCompute(renderGraph, in m_ActiveCameraDepthTexture, out m_DepthTexture, ref renderingData);
        }
        else
        {
            RenderingUtils.SetGlobalRenderGraphTextureName(renderGraph, "_CameraDepthTexture", SystemInfo.usesReversedZBuffer ? renderGraph.defaultResources.blackTexture : renderGraph.defaultResources.whiteTexture, "SetDefaultGlobalCameraDepthTexture");
        }

        // Scalable Ambient Obscurance
        if (supportIntermediateRendering && m_PipelineAsset.saoEnabled)
        {
            m_ScalableAO.RecordRenderGraph(renderGraph, in m_DepthTexture, out m_ScalableAOTexture, ref renderingData);
        }
        else
        {
            RenderingUtils.SetGlobalRenderGraphTextureName(renderGraph, "_ScreenSpaceOcclusionTexture", renderGraph.defaultResources.whiteTexture);
        }

        // SSR
        // TODO: investigate why ssr is jittering when taa enabled
        if (supportIntermediateRendering && m_PipelineAsset.ssrEnabled)
        {
            m_ScreenSpaceReflection.RecordRenderGraph(renderGraph, in m_ActiveCameraDepthTexture, ref renderingData);
        }
        else
        {
            RenderingUtils.SetGlobalRenderGraphTextureName(renderGraph, "_SsrTexture", renderGraph.defaultResources.blackTexture, "Set Global SSR Texture");
        }

        // Draw opaque objects
        m_ForwardOpaque.RecordRenderGraph(renderGraph, m_ActiveCameraColorTexture, m_ActiveCameraDepthTexture, m_MainLightShadowMapTexture, m_AdditionalLightShadowMapTexture, m_ScalableAOTexture, ref renderingData);

        // Draw skybox
        m_DrawSkybox.RecordRenderGraph(renderGraph, m_ActiveCameraColorTexture, m_ActiveCameraDepthTexture, ref renderingData);

        // Copy ssr history color
        if (supportIntermediateRendering && m_PipelineAsset.ssrEnabled)
        {
            m_CopyColor.CopySsrHistory(renderGraph, in m_ActiveCameraColorTexture, ref FrameHistory.s_SsrHistoryColorRT, ref renderingData);
        }

        // // Copy color pass
        // if (supportIntermediateRendering)
        // {
        //     m_CopyColor.RecordRenderGraphCompute(renderGraph, in m_ActiveCameraColorTexture, out m_CameraColorTexture, ref renderingData);
        // }
        // else
        // {
        //     RenderingUtils.SetGlobalRenderGraphTextureName(renderGraph, "_CameraColorTexture", renderGraph.defaultResources.whiteTexture);
        // }

        // Draw transparent objects
        m_ForwardTransparent.RecordRenderGraph(renderGraph, m_ActiveCameraColorTexture, m_ActiveCameraDepthTexture, TextureHandle.nullHandle, TextureHandle.nullHandle, m_ScalableAOTexture, ref renderingData);

        DrawRenderGraphGizmos(renderGraph, m_ActiveCameraColorTexture, m_ActiveCameraDepthTexture, GizmoSubset.PreImageEffects, ref renderingData);

        // TAA
        if (supportIntermediateRendering && (renderingData.antialiasing == AntialiasingMode.TemporalAntialiasing))
        {
            var target = nextCameraColorTexture;
            m_TemporalAA.RecordRenderGraph(renderGraph, in m_ActiveCameraColorTexture, ref target, ref renderingData);
            m_ActiveCameraColorTexture = target;
        }

        bool fxaaEnabled = supportIntermediateRendering && (renderingData.antialiasing == AntialiasingMode.FastApproximateAntialiasing);

        if (applyPostProcessing)
        {
            var target = !fxaaEnabled ? m_BackbufferColorTexture : nextCameraColorTexture;
            m_PostProcess.RecordRenderGraph(renderGraph, in m_ActiveCameraColorTexture, m_ColorGradingLutTexture, target, ref renderingData);

            m_ActiveCameraColorTexture = target;
            if (!fxaaEnabled)
            {
                m_IsActiveTargetBackbuffer = true;
            }
        }

        if (fxaaEnabled)
        {
            m_FastApproximateAA.RecordRenderGraph(renderGraph, m_ActiveCameraColorTexture, m_BackbufferColorTexture, ref renderingData);
            m_ActiveCameraColorTexture = m_BackbufferColorTexture;
            m_IsActiveTargetBackbuffer = true;
        }

        if (supportIntermediateRendering && !m_IsActiveTargetBackbuffer)
        {
            m_FinalBlit.RecordRenderGraph(renderGraph, m_ActiveCameraColorTexture, m_BackbufferColorTexture, ref renderingData);
            m_ActiveCameraColorTexture = m_BackbufferColorTexture;
            m_IsActiveTargetBackbuffer = true;
        }

#if UNITY_EDITOR
        bool isGizmosEnabled = Handles.ShouldRenderGizmos();
        bool isSceneViewOrPreviewCamera = cameraType == CameraType.SceneView || cameraType == CameraType.Preview;
        if (supportIntermediateRendering && (isSceneViewOrPreviewCamera || isGizmosEnabled))
        {
            m_FinalCopyDepthToCameraTarget.RecordRenderGraph(renderGraph, m_ActiveCameraDepthTexture, m_BackbufferDepthTexture, m_ActiveCameraColorTexture, ref renderingData, false, "FinalDepthCopy");
        }
#endif

        // Drawing Gizmos
        DrawRenderGraphGizmos(renderGraph, m_ActiveCameraColorTexture, m_BackbufferDepthTexture, GizmoSubset.PostImageEffects, ref renderingData);
    }

    private void CreateAndSetCameraRenderTargets(RenderGraph renderGraph, ref RenderingData renderingData, bool supportIntermediateRendering)
    {
        var camera = renderingData.cameraData.camera;

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
            importInfo.format = renderingData.cameraData.defaultGraphicsFormat;
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

        var cameraTargetDescriptor = renderingData.cameraData.targetDescriptor;
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

        var depthDescriptor = renderingData.cameraData.targetDescriptor;
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

                cmd.SetupCameraProperties(data.renderingData.cameraData.camera);

                // This is still required because of the following reasons:
                // - Camera billboard properties.
                // - Camera frustum planes: unity_CameraWorldClipPlanes[6]
                // - _ProjectionParams.x logic is deep inside GfxDevice
                SetPerCameraShaderVariables(cmd, ref data);
            });
        }
    }

    private static void SetPerCameraShaderVariables(RasterCommandBuffer cmd, ref PassData passData)
    {
        // data.renderingData.camera, RenderingUtils.IsHandleYFlipped(data.colorTarget, data.renderingData.camera)
        ref var renderingData = ref passData.renderingData;
        ref var cameraData = ref renderingData.cameraData;
        var camera = cameraData.camera;
        bool isTargetFlipped = RenderingUtils.IsHandleYFlipped(passData.colorTarget, camera);

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

        cmd.SetGlobalVector(ShaderPropertyIDs.WorldSpaceCameraPos, cameraData.worldSpaceCameraPos);
        cmd.SetGlobalVector(ShaderPropertyIDs.ZBufferParams, zBufferParams);
        float aspectRatio = (float)camera.pixelWidth / (float)camera.pixelHeight;
        float orthographicSize = camera.orthographicSize;
        Vector4 orthoParams = new Vector4(orthographicSize * aspectRatio, orthographicSize, 0.0f, isOrthographic);
        cmd.SetGlobalVector(ShaderPropertyIDs.OrthoParams, orthoParams);
        float projectionFlipSign = isTargetFlipped ? -1.0f : 1.0f;
        cmd.SetGlobalVector(ShaderPropertyIDs.ProjectionParams, new Vector4(projectionFlipSign, near, far, invFar));
        cmd.SetGlobalVector(ShaderPropertyIDs.ScreenParams, new Vector4(cameraWidth, cameraHeight, 1.0f + 1.0f / cameraWidth, 1.0f + 1.0f / cameraHeight));

        // Set view matrix and projection matrix (jittered and non-gpu)
        cmd.SetViewProjectionMatrices(FrameHistory.GetCurrentFrameView(), FrameHistory.GetCurrentFrameJitteredProjection());
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
            passData.rendererList = renderGraph.CreateGizmoRendererList(renderingData.cameraData.camera, gizmoSubset);

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
