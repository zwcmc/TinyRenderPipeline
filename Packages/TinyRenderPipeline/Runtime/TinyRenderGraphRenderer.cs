using UnityEditor;
using UnityEngine;
using System.Diagnostics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

public class TinyRenderGraphRenderer : TinyBaseRenderer
{
#if UNITY_EDITOR
    private static readonly ProfilingSampler s_DrawGizmosRenderGraphPassSampler = new ProfilingSampler("DrawGizmosPass");
#endif
    private static readonly ProfilingSampler s_SetupCameraPropertiesSampler = new ProfilingSampler("SetupCameraProperties");

    private RTHandle m_TargetColorHandle;
    private RTHandle m_TargetDepthHandle;
    private TextureHandle m_BackBufferColor;
    private TextureHandle m_BackBufferDepth;

    private TextureHandle m_ActiveRenderGraphCameraColorHandle;
    private TextureHandle m_ActiveRenderGraphCameraDepthHandle;

    private static RTHandle[] m_CameraColorHandles = new RTHandle[] { null, null };
    private static RTHandle m_CameraDepthHandle;
    private static TextureHandle[] m_RenderGraphCameraColorHandles = new TextureHandle[] { TextureHandle.nullHandle, TextureHandle.nullHandle };
    private static TextureHandle m_RenderGraphCameraDepthHandle;

    private static int m_CurrentColorHandle = 0;
    private TextureHandle currentRenderGraphCameraColorHandle => (m_RenderGraphCameraColorHandles[m_CurrentColorHandle]);

    private TextureHandle nextRenderGraphCameraColorHandle
    {
        get
        {
            m_CurrentColorHandle = (m_CurrentColorHandle + 1) % 2;
            return m_RenderGraphCameraColorHandles[m_CurrentColorHandle];
        }
    }

    private TextureHandle m_MainLightShadowmapTextureHdl;
    private TextureHandle m_AdditionalLightsShadowmapTextureHdl;

    private TextureHandle m_DepthTextureHdl;
    private TextureHandle m_OpaqueColorTextureHdl;

    private TextureHandle m_ColorGradingLutTextureHdl;

    private bool m_IsActiveTargetBackBuffer;

    public TinyRenderGraphRenderer(TinyRenderPipelineAsset asset) : base(asset)
    {
        m_IsActiveTargetBackBuffer = true;
    }

    private void RecordRenderGraph(RenderGraph renderGraph, ref RenderingData renderingData)
    {
        var camera = renderingData.camera;
        var cameraType = camera.cameraType;

        // Setup lights data
        forwardLights.SetupRenderGraphLights(renderGraph, ref renderingData);

        // Main light shadowmap
        if (mainLightShadowPass.Setup(ref renderingData))
        {
            m_MainLightShadowmapTextureHdl = mainLightShadowPass.Record(renderGraph, ref renderingData);
        }

        // Additional lights shadowmap
        if (additionalLightsShadowPass.Setup(ref renderingData))
        {
            m_AdditionalLightsShadowmapTextureHdl = additionalLightsShadowPass.Record(renderGraph, ref renderingData);
        }

        // Post processing
        // Check post processing data setup
        var additionalCameraData = camera.GetComponent<AdditionalCameraData>();
        var postProcessingData = additionalCameraData ? (additionalCameraData.isOverridePostProcessingData ? additionalCameraData.overridePostProcessingData : renderingData.postProcessingData) : renderingData.postProcessingData;
        bool applyPostProcessing = postProcessingData != null;
        // Only game camera and scene camera have post processing effects
        applyPostProcessing &= cameraType <= CameraType.SceneView;
        // Check if disable post processing effects in scene view
        applyPostProcessing &= CoreUtils.ArePostProcessesEnabled(camera);

        // Color grading generating LUT pass
        bool generateColorGradingLut = applyPostProcessing && renderingData.isHdrEnabled;
        if (generateColorGradingLut)
        {
            colorGradingLutPass.Record(renderGraph, out m_ColorGradingLutTextureHdl, postProcessingData, ref renderingData);
        }

        bool needCopyColor = renderingData.copyColorTexture;
        if (additionalCameraData)
            needCopyColor &= additionalCameraData.requireColorTexture;

        bool createColorTexture = applyPostProcessing; // post-processing is active
        createColorTexture |= !renderingData.isDefaultCameraViewport; // camera's Viewport Rect is not full screen (full screen Viewport Rect=(0, 0, 1, 1))
        createColorTexture |= needCopyColor; // copy color texture is enabled

        bool needCopyDepth = renderingData.copyDepthTexture;
        if (additionalCameraData)
            needCopyDepth &= additionalCameraData.requireDepthTexture;

        bool useRenderScale = renderingData.renderScale < 1.0f || renderingData.renderScale > 1.0f;
        bool intermediateRenderTexture = createColorTexture || needCopyDepth || useRenderScale;

        CreateRenderGraphCameraRenderTargets(renderGraph, ref renderingData, intermediateRenderTexture);

        // Setup camera properties
        SetupRenderGraphCameraProperties(renderGraph, ref renderingData, m_BackBufferColor);

        renderOpaqueForwardPass.Record(renderGraph, m_ActiveRenderGraphCameraColorHandle, m_ActiveRenderGraphCameraDepthHandle, m_MainLightShadowmapTextureHdl, m_AdditionalLightsShadowmapTextureHdl, ref renderingData);

        // Copy depth texture if needed after rendering opaque objects
        if (needCopyDepth)
        {
            var depthDescriptor = renderingData.cameraTargetDescriptor;
            depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
            depthDescriptor.depthStencilFormat = GraphicsFormat.None;
            depthDescriptor.depthBufferBits = (int)DepthBits.None;
            m_DepthTextureHdl = RenderingUtils.CreateRenderGraphTexture(renderGraph, depthDescriptor, "_CameraDepthTexture", true);
            copyDepthPass.Record(renderGraph, m_ActiveRenderGraphCameraDepthHandle, m_DepthTextureHdl, TextureHandle.nullHandle, ref renderingData, true);
        }
        else
        {
            RenderingUtils.SetGlobalRenderGraphTextureName(renderGraph, "_CameraDepthTexture", SystemInfo.usesReversedZBuffer ? renderGraph.defaultResources.blackTexture : renderGraph.defaultResources.whiteTexture, "SetDefaultGlobalCameraDepthTexture");
        }

        renderSkyboxPass.DrawRenderGraphSkybox(renderGraph, m_ActiveRenderGraphCameraColorHandle, m_ActiveRenderGraphCameraDepthHandle, ref renderingData);

        // Copy color texture if needed after rendering skybox
        if (needCopyColor)
        {
            var colorDescriptor = renderingData.cameraTargetDescriptor;
            colorDescriptor.depthBufferBits = (int)DepthBits.None;
            colorDescriptor.depthStencilFormat = GraphicsFormat.None;
            m_OpaqueColorTextureHdl = RenderingUtils.CreateRenderGraphTexture(renderGraph, colorDescriptor, "_CameraOpaqueTexture", true, FilterMode.Bilinear);
            copyColorPass.Record(renderGraph, m_ActiveRenderGraphCameraColorHandle, m_OpaqueColorTextureHdl, ref renderingData);
        }
        else
        {
            RenderingUtils.SetGlobalRenderGraphTextureName(renderGraph, "_CameraOpaqueTexture", renderGraph.defaultResources.whiteTexture);
        }

        renderTransparentForwardPass.Record(renderGraph, m_ActiveRenderGraphCameraColorHandle, m_ActiveRenderGraphCameraDepthHandle, TextureHandle.nullHandle, TextureHandle.nullHandle, ref renderingData);

        DrawRenderGraphGizmos(renderGraph, m_ActiveRenderGraphCameraColorHandle, m_ActiveRenderGraphCameraDepthHandle, GizmoSubset.PreImageEffects, ref renderingData);

        // FXAA is enabled only if post-processing is enabled
        bool hasFxaaPass = applyPostProcessing && (postProcessingData.antialiasingMode == PostProcessingData.AntialiasingMode.FastApproximateAntialiasing);
        // Check post-processing pass need to resolve to final camera target
        bool resolvePostProcessingToCameraTarget = !hasFxaaPass;

        if (applyPostProcessing)
        {
            // After post-processing pass, blit final target to Camera Target if FXAA is disabled
            // if FXAA is enabled, blit final target to another intermediate render texture
            var target = resolvePostProcessingToCameraTarget ? m_BackBufferColor : nextRenderGraphCameraColorHandle;

            postProcessingPass.Record(renderGraph, in m_ActiveRenderGraphCameraColorHandle, m_ColorGradingLutTextureHdl, target, resolvePostProcessingToCameraTarget, postProcessingData, ref renderingData);

            // Camera color handle has resolved to camera target, set active camera color handle to camera color target;
            // If not resolved to camera target, set active camera color handle to another intermediate render texture handle, just like swap RenderTargetBufferSystem in TinyRenderer(not using Render Graph);
            if (resolvePostProcessingToCameraTarget)
            {
                m_ActiveRenderGraphCameraColorHandle = m_BackBufferColor;
                m_IsActiveTargetBackBuffer = true;
            }
            else
            {
                m_ActiveRenderGraphCameraColorHandle = target;
            }
        }

        // FXAA pass always blit to final camera target
        if (hasFxaaPass)
        {
            fxaaPass.Record(renderGraph, m_ActiveRenderGraphCameraColorHandle, m_BackBufferColor, postProcessingData, ref renderingData);
            m_ActiveRenderGraphCameraColorHandle = m_BackBufferColor;
            m_IsActiveTargetBackBuffer = true;
        }

        // Check active color attachment is resolved to final target:
        // 1. FXAA pass is enabled, it always blit active color attachment to final camera target
        // 2. Post-processing is enabled and FXAA pass is disabled, active color attachment apply post-processing effects and then blit it to final camera target
        // 3. Active color attachment is the final camera target
        bool cameraTargetResolved = hasFxaaPass || (applyPostProcessing && resolvePostProcessingToCameraTarget) || m_IsActiveTargetBackBuffer;

        // If is not resolved to final camera target, need final blit pass to do this
        if (!cameraTargetResolved)
        {
            finalBlitPass.Record(renderGraph, m_ActiveRenderGraphCameraColorHandle, m_BackBufferColor, ref renderingData);
            m_ActiveRenderGraphCameraColorHandle = m_BackBufferColor;
            m_IsActiveTargetBackBuffer = true;
        }

        // When use intermediate rendering, copying depth to the camera target finally to make gizmos render correctly in scene camera view or preview camera view
#if UNITY_EDITOR
        bool isGizmosEnabled = Handles.ShouldRenderGizmos();
        bool isSceneViewOrPreviewCamera = cameraType == CameraType.SceneView || cameraType == CameraType.Preview;
        if (intermediateRenderTexture && (isSceneViewOrPreviewCamera || isGizmosEnabled))
        {
            finalDepthCopyPass.Record(renderGraph, m_ActiveRenderGraphCameraDepthHandle, m_BackBufferDepth, m_ActiveRenderGraphCameraColorHandle, ref renderingData, false, "FinalDepthCopy");
        }
#endif

        // Drawing Gizmos
        DrawRenderGraphGizmos(renderGraph, m_ActiveRenderGraphCameraColorHandle, m_BackBufferDepth, GizmoSubset.PostImageEffects, ref renderingData);
    }

    private void CreateRenderGraphCameraRenderTargets(RenderGraph renderGraph, ref RenderingData renderingData, bool intermediateRenderTexture)
    {
        var camera = renderingData.camera;

        bool isBuiltInTexture = (camera.targetTexture == null);
        RenderTargetIdentifier targetColorId = isBuiltInTexture ? BuiltinRenderTextureType.CameraTarget : new RenderTargetIdentifier(camera.targetTexture);
        RenderTargetIdentifier targetDepthId = isBuiltInTexture ? BuiltinRenderTextureType.Depth : new RenderTargetIdentifier(camera.targetTexture);

        if (m_TargetColorHandle == null)
        {
            m_TargetColorHandle = RTHandles.Alloc(targetColorId, "Backbuffer color");
        }
        else if (m_TargetColorHandle.nameID != targetColorId)
        {
            RTHandleStaticHelpers.SetRTHandleUserManagedWrapper(ref m_TargetColorHandle, targetColorId);
        }

        if (m_TargetDepthHandle == null)
        {
            m_TargetDepthHandle = RTHandles.Alloc(targetDepthId, "Backbuffer depth");
        }
        else if (m_TargetDepthHandle.nameID != targetDepthId)
        {
            RTHandleStaticHelpers.SetRTHandleUserManagedWrapper(ref m_TargetDepthHandle, targetDepthId);
        }

        RenderTargetInfo importInfo = new RenderTargetInfo();
        RenderTargetInfo importInfoDepth = new RenderTargetInfo();
        if (isBuiltInTexture)
        {
            importInfo.width = Screen.width;
            importInfo.height = Screen.height;
            importInfo.volumeDepth = 1;
            importInfo.msaaSamples = 1;
            // The editor always allocates the system rendertarget with a single msaa sample
            // See: ConfigureTargetTexture in PlayModeView.cs
            if (Application.isEditor)
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
        if (intermediateRenderTexture)
        {
            if (clearFlags > CameraClearFlags.Color)
                clearFlags = CameraClearFlags.Color;
        }

        // If using intermediate rendering, do dot clear on first use in case of a camera with a Viewport Rect smaller than the full screen.
        bool clearColor = clearFlags <= CameraClearFlags.Color && !intermediateRenderTexture;
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

        m_BackBufferColor = renderGraph.ImportTexture(m_TargetColorHandle, importInfo, importBackbufferColorParams);
        m_BackBufferDepth = renderGraph.ImportTexture(m_TargetDepthHandle, importInfoDepth, importBackbufferDepthParams);

        if (intermediateRenderTexture)
        {
            var cameraTargetDescriptor = renderingData.cameraTargetDescriptor;
            cameraTargetDescriptor.depthBufferBits = (int)DepthBits.None;

            RenderingUtils.ReAllocateIfNeeded(ref m_CameraColorHandles[0], cameraTargetDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_CameraTargetAttachmentA");
            RenderingUtils.ReAllocateIfNeeded(ref m_CameraColorHandles[1], cameraTargetDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_CameraTargetAttachmentB");

            ImportResourceParams importColorParams = new ImportResourceParams();
            // Always clear on first use for intermediate render texture
            importColorParams.clearOnFirstUse = true;
            importColorParams.clearColor = backgroundColor.linear;
            importColorParams.discardOnLastUse = true;

            m_CurrentColorHandle = 0;

            m_RenderGraphCameraColorHandles[0] = renderGraph.ImportTexture(m_CameraColorHandles[0], importColorParams);
            m_RenderGraphCameraColorHandles[1] = renderGraph.ImportTexture(m_CameraColorHandles[1], importColorParams);

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
            m_RenderGraphCameraDepthHandle = renderGraph.ImportTexture(m_CameraDepthHandle, importDepthParams);
        }

        m_ActiveRenderGraphCameraColorHandle = intermediateRenderTexture ? currentRenderGraphCameraColorHandle : m_BackBufferColor;
        m_IsActiveTargetBackBuffer = intermediateRenderTexture ? false : true;
        m_ActiveRenderGraphCameraDepthHandle = intermediateRenderTexture ? m_RenderGraphCameraDepthHandle : m_BackBufferDepth;
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

        using (var builder = renderGraph.AddRasterRenderPass<DrawGizmosPassData>(s_DrawGizmosRenderGraphPassSampler.name, out var passData, s_DrawGizmosRenderGraphPassSampler))
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

    private class PassData
    {
        public TextureHandle colorTarget;
        public RenderingData renderingData;
    }

    private void SetupRenderGraphCameraProperties(RenderGraph renderGraph, ref RenderingData renderingData, TextureHandle colorTarget)
    {
        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_SetupCameraPropertiesSampler.name, out var passData, s_SetupCameraPropertiesSampler))
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

    public void RecordAndExecuteRenderGraph(RenderGraph renderGraph, ref RenderingData renderingData)
    {
        var context = renderingData.renderContext;
        RenderGraphParameters renderGraphParameters = new RenderGraphParameters()
        {
            executionName = TinyRenderPipeline.Profiling.TryGetOrAddCameraSampler(renderingData.camera).name,
            commandBuffer = renderingData.commandBuffer,
            scriptableRenderContext = context,
            currentFrameIndex = Time.frameCount
        };

        var executor = renderGraph.RecordAndExecute(renderGraphParameters);
        RecordRenderGraph(renderGraph, ref renderingData);
        executor.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);


        m_CameraColorHandles[0]?.Release();
        m_CameraColorHandles[1]?.Release();
        m_CameraDepthHandle?.Release();
    }
}
