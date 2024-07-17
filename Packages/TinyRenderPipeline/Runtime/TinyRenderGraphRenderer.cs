using UnityEditor;
using UnityEngine;
using System.Diagnostics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

public class TinyRenderGraphRenderer : TinyBaseRenderer
{
#if UNITY_EDITOR
    private static readonly ProfilingSampler m_DrawGizmosRenderGraphPassSampler = new ProfilingSampler("DrawGizmosPass");
#endif

    private RTHandle m_TargetColorHandle;
    private RTHandle m_TargetDepthHandle;
    private TextureHandle m_BackBufferColor;
    private TextureHandle m_BackBufferDepth;

    private TextureHandle m_ActiveRenderGraphCameraColorHandle;
    private TextureHandle m_ActiveRenderGraphCameraDepthHandle;

    private static RTHandle[] m_CameraColorHandles = new RTHandle[] { null, null };
    private static RTHandle m_CameraDepthHandle;
    private static TextureHandle[] m_RenderGraphCameraColorHandles = new TextureHandle[] { new TextureHandle(), new TextureHandle() };
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

    // Passes
    private DrawSkyboxPass m_DrawSkyboxPass;

    public TinyRenderGraphRenderer()
    {
        m_DrawSkyboxPass = new DrawSkyboxPass();
    }

    private void RecordRenderGraph(RenderGraph renderGraph, ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var camera = renderingData.camera;
        var cameraType = camera.cameraType;

        var additionalCameraData = camera.GetComponent<AdditionalCameraData>();
        var postProcessingData = additionalCameraData ?
            (additionalCameraData.isOverridePostProcessingData ? additionalCameraData.overridePostProcessingData : renderingData.postProcessingData) :
            renderingData.postProcessingData;
        bool applyPostProcessing = postProcessingData != null;
        // Only game camera and scene camera have post processing effects
        applyPostProcessing &= cameraType <= CameraType.SceneView;
        // Check if disable post processing effects in scene view
        applyPostProcessing &= CoreUtils.ArePostProcessesEnabled(camera);

        // Color grading generating LUT pass
        bool generateColorGradingLut = applyPostProcessing && renderingData.isHdrEnabled;
        if (generateColorGradingLut)
        {
            // to-do: m_ColorGradingLutPass
        }

        bool needCopyColor = renderingData.copyColorTexture;
        if (additionalCameraData)
            needCopyColor &= additionalCameraData.requireColorTexture;

        bool createColorTexture = applyPostProcessing; // post-processing is active
        createColorTexture |= !renderingData.isDefaultCameraViewport; // camera's viewport rect is not default(0, 0, 1, 1)
        createColorTexture |= needCopyColor; // copy color texture is enabled

        bool needCopyDepth = renderingData.copyDepthTexture;
        if (additionalCameraData)
            needCopyDepth &= additionalCameraData.requireDepthTexture;

        bool useRenderScale = renderingData.renderScale < 1.0f || renderingData.renderScale > 1.0f;
        bool intermediateRenderTexture = createColorTexture || needCopyDepth || useRenderScale;

        CreateRenderGraphCameraRenderTargets(renderGraph, ref renderingData, intermediateRenderTexture);

        // Setup camera properties
        SetupRenderGraphCameraProperties(renderGraph, ref renderingData, m_BackBufferColor);

        // to-do: m_RenderOpaqueForwardPass

        // to-do: m_CopyDepthPass if needed

        m_DrawSkyboxPass.DrawRenderGraphSkybox(renderGraph, m_ActiveRenderGraphCameraColorHandle, m_ActiveRenderGraphCameraDepthHandle, ref renderingData);

        // to-do: m_CopyColorPass if needed

        // to-do: m_RenderTransparentForwardPass

        DrawRenderGraphGizmos(renderGraph, m_ActiveRenderGraphCameraColorHandle, m_ActiveRenderGraphCameraDepthHandle, GizmoSubset.PreImageEffects, ref renderingData);

        // FXAA is enabled only if post-processing is enabled
        bool hasFxaaPass = applyPostProcessing && (postProcessingData.antialiasingMode == PostProcessingData.AntialiasingMode.FastApproximateAntialiasing);
        // Check post-processing pass need to resolve to final camera target
        bool resolvePostProcessingToCameraTarget = !hasFxaaPass;

        if (applyPostProcessing)
        {
            // to-do: m_PostProcessingPass
        }

        // FXAA pass always blit to final camera target
        if (hasFxaaPass)
        {
            // to-do: m_FXAAPass
        }

        // Check active color attachment is resolved to final target:
        // 1. FXAA pass is enabled, it always blit active color attachment to final camera target
        // 2. Post-processing is enabled and FXAA pass is disabled, active color attachment apply post-processing effects and then blit it to final camera target


        // 3. Active color attachment is the final camera target
        // RTHandle handle0 = m_ActiveRenderGraphCameraColorHandle;
        // RTHandle handle1 = m_BackBufferColor;
        //  || (handle0.nameID == handle1.nameID)
        bool cameraTargetResolved = hasFxaaPass || (applyPostProcessing && resolvePostProcessingToCameraTarget);

        // If is not resolved to final camera target, need final blit pass to do this
        if (!cameraTargetResolved)
        {
            // to-do: m_FinalBlitPass
        }

#if UNITY_EDITOR
        // to-do: m_FinalDepthCopyPass
#endif

        DrawRenderGraphGizmos(renderGraph, m_ActiveRenderGraphCameraColorHandle, m_ActiveRenderGraphCameraDepthHandle, GizmoSubset.PostImageEffects, ref renderingData);
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
            importInfo.msaaSamples = Screen.msaaSamples;
            // The editor always allocates the system rendertarget with a single msaa sample
            // See: ConfigureTargetTexture in PlayModeView.cs
            if (Application.isEditor)
                importInfo.msaaSamples = 1;
            importInfo.format = renderingData.isHdrEnabled ? SystemInfo.GetGraphicsFormat(DefaultFormat.HDR) : SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);
            importInfoDepth = importInfo;
            importInfoDepth.format = SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil);
        }
        else
        {
            var targetTexture = camera.targetTexture;
            importInfo.width = targetTexture.width;
            importInfo.height = targetTexture.height;
            importInfo.volumeDepth = targetTexture.volumeDepth;
            importInfo.msaaSamples = targetTexture.antiAliasing;
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

        m_BackBufferColor = renderGraph.ImportTexture(m_TargetColorHandle, importInfo, importBackbufferColorParams);
        m_BackBufferDepth = renderGraph.ImportTexture(m_TargetDepthHandle, importInfoDepth, importBackbufferDepthParams);

        if (intermediateRenderTexture)
        {
            var cameraTargetDescriptor = renderingData.cameraTargetDescriptor;
            cameraTargetDescriptor.useMipMap = false;
            cameraTargetDescriptor.autoGenerateMips = false;
            cameraTargetDescriptor.depthBufferBits = (int)DepthBits.None;

            RenderingUtils.ReAllocateIfNeeded(ref m_CameraColorHandles[0], cameraTargetDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_CameraTargetAttachmentA");
            RenderingUtils.ReAllocateIfNeeded(ref m_CameraColorHandles[1], cameraTargetDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_CameraTargetAttachmentB");

            ImportResourceParams importColorParams = new ImportResourceParams();
            importColorParams.clearOnFirstUse = clearColor;
            importColorParams.clearColor = backgroundColor.linear;
            importColorParams.discardOnLastUse = true;

            m_CurrentColorHandle = 0;

            m_RenderGraphCameraColorHandles[0] = renderGraph.ImportTexture(m_CameraColorHandles[0], importColorParams);
            m_RenderGraphCameraColorHandles[1] = renderGraph.ImportTexture(m_CameraColorHandles[1], importColorParams);

            var depthDescriptor = renderingData.cameraTargetDescriptor;
            depthDescriptor.useMipMap = false;
            depthDescriptor.autoGenerateMips = false;
            depthDescriptor.bindMS = false;

            depthDescriptor.graphicsFormat = GraphicsFormat.None;
            depthDescriptor.depthStencilFormat = GraphicsFormat.D32_SFloat_S8_UInt;
            depthDescriptor.depthBufferBits = 32;

            RenderingUtils.ReAllocateIfNeeded(ref m_CameraDepthHandle, depthDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_CameraDepthAttachment");

            ImportResourceParams importDepthParams = new ImportResourceParams();
            importDepthParams.clearOnFirstUse = clearDepth;
            importDepthParams.clearColor = backgroundColor.linear;
            importDepthParams.discardOnLastUse = true;
            m_RenderGraphCameraDepthHandle = renderGraph.ImportTexture(m_CameraDepthHandle, importDepthParams);
        }

        m_ActiveRenderGraphCameraColorHandle = intermediateRenderTexture ? currentRenderGraphCameraColorHandle : m_BackBufferColor;
        m_ActiveRenderGraphCameraDepthHandle = intermediateRenderTexture ? m_RenderGraphCameraDepthHandle : m_BackBufferDepth;
    }

    private class DrawGizmosPassData
    {
        public RendererListHandle rendererList;
    }

    [Conditional("UNITY_EDITOR")]
    private void DrawRenderGraphGizmos(RenderGraph renderGraph, TextureHandle color, TextureHandle depth, GizmoSubset gizmoSubset, ref RenderingData renderingData)
    {
#if UNITY_EDITOR
        if (!Handles.ShouldRenderGizmos())
            return;

        using (var builder = renderGraph.AddRasterRenderPass<DrawGizmosPassData>("Draw Gizmos Pass", out var passData, m_DrawGizmosRenderGraphPassSampler))
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
        public TinyBaseRenderer renderer;
        public TextureHandle colorTarget;
        public RenderingData renderingData;
    }

    private void SetupRenderGraphCameraProperties(RenderGraph renderGraph, ref RenderingData renderingData, TextureHandle colorTarget)
    {
        using (var builder = renderGraph.AddRasterRenderPass<PassData>("Setup Camera Properties", out var passData))
        {
            passData.renderer = this;
            passData.colorTarget = colorTarget;
            passData.renderingData = renderingData;

            // Not allow pass culling
            builder.AllowPassCulling(false);
            // Enable this to set global state
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext rasterGraphContext) =>
            {
                rasterGraphContext.cmd.SetupCameraProperties(data.renderingData.camera);
                data.renderer.SetPerCameraShaderVariables(rasterGraphContext.cmd, data.renderingData.camera, RenderingUtils.IsHandleYFlipped(data.colorTarget, data.renderingData.camera));
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

        using (renderGraph.RecordAndExecute(renderGraphParameters))
        {
            RecordRenderGraph(renderGraph, context, ref renderingData);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        m_CameraColorHandles[0]?.Release();
        m_CameraColorHandles[1]?.Release();
        m_CameraDepthHandle?.Release();
    }
}
