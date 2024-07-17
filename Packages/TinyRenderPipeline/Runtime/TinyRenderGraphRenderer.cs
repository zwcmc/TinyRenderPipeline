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

    private static RTHandle[] m_RenderGraphCameraColorHandles = new RTHandle[] { null, null };
    private static RTHandle m_RenderGraphCameraDepthHandle;
    private static int m_CurrentColorHandle = 0;

    private RTHandle currentRenderGraphCameraColorHandle => (m_RenderGraphCameraColorHandles[m_CurrentColorHandle]);

    private RTHandle nextRenderGraphCameraColorHandle
    {
        get
        {
            m_CurrentColorHandle = (m_CurrentColorHandle + 1) % 2;
            return m_RenderGraphCameraColorHandles[m_CurrentColorHandle];
        }
    }

    private DrawSkyboxPass m_DrawSkyboxPass;

    public TinyRenderGraphRenderer()
    {
        m_DrawSkyboxPass = new DrawSkyboxPass();
    }

    private void RecordRenderGraph(RenderGraph renderGraph, ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var camera = renderingData.camera;
        var cameraType = camera.cameraType;

        #region UseIntermediateRenderTexture

        var additionalCameraData = camera.GetComponent<AdditionalCameraData>();
        var postProcessingData = additionalCameraData ?
            (additionalCameraData.isOverridePostProcessingData ? additionalCameraData.overridePostProcessingData : renderingData.postProcessingData) :
            renderingData.postProcessingData;
        bool applyPostProcessing = postProcessingData != null;
        // Only game camera and scene camera have post processing effects
        applyPostProcessing &= cameraType <= CameraType.SceneView;
        // Check if disable post processing effects in scene view
        applyPostProcessing &= CoreUtils.ArePostProcessesEnabled(camera);

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

        #endregion

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
        RenderTargetInfo importInfoDepth;

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

        }

        // Setup camera properties
        SetupRenderGraphCameraProperties(renderGraph, ref renderingData, m_BackBufferColor);

        DrawRenderGraphGizmos(renderGraph, m_BackBufferColor, m_BackBufferDepth, GizmoSubset.PreImageEffects, ref renderingData);

        m_DrawSkyboxPass.DrawRenderGraphSkybox(renderGraph, m_BackBufferColor, m_BackBufferDepth, ref renderingData);

        DrawRenderGraphGizmos(renderGraph, m_BackBufferColor, m_BackBufferDepth, GizmoSubset.PostImageEffects, ref renderingData);
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

        m_RenderGraphCameraColorHandles[0]?.Release();
        m_RenderGraphCameraColorHandles[1]?.Release();
        m_RenderGraphCameraDepthHandle?.Release();
    }

    public static TextureHandle CreateRenderGraphTexture(RenderGraph renderGraph, RenderTextureDescriptor desc, string name, bool clear,
        FilterMode filterMode = FilterMode.Point, TextureWrapMode wrapMode = TextureWrapMode.Clamp)
    {
        TextureDesc rgDesc = new TextureDesc(desc.width, desc.height);
        rgDesc.dimension = desc.dimension;
        rgDesc.clearBuffer = clear;
        rgDesc.bindTextureMS = desc.bindMS;
        rgDesc.colorFormat = desc.graphicsFormat;
        rgDesc.depthBufferBits = (DepthBits)desc.depthBufferBits;
        rgDesc.slices = desc.volumeDepth;
        rgDesc.msaaSamples = (MSAASamples)desc.msaaSamples;
        rgDesc.name = name;
        rgDesc.enableRandomWrite = desc.enableRandomWrite;
        rgDesc.filterMode = filterMode;
        rgDesc.wrapMode = wrapMode;
        rgDesc.isShadowMap = desc.shadowSamplingMode != ShadowSamplingMode.None && desc.depthStencilFormat != GraphicsFormat.None;
        // TODO RENDERGRAPH: depthStencilFormat handling?

        return renderGraph.CreateTexture(rgDesc);
    }
}
