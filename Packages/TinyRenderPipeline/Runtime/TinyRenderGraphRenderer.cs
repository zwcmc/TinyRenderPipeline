using UnityEditor;
using UnityEngine;
using System.Diagnostics;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using Debug = UnityEngine.Debug;

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

    private ForwardLights m_ForwardLights;
    private MainLightShadowPass m_MainLightShadowPass;
    private AdditionalLightsShadowPass m_AdditionalLightsShadowPass;

    // Passes
    private DrawObjectsForwardPass m_RenderOpaqueForwardPass;
    private CopyDepthPass m_CopyDepthPass;
    private DrawSkyboxPass m_DrawSkyboxPass;
    private CopyColorPass m_CopyColorPass;
    private DrawObjectsForwardPass m_RenderTransparentForwardPass;
    private PostProcessingPass m_PostProcessingPass;
    private FinalBlitPass m_FinalBlitPass;

#if UNITY_EDITOR
    private CopyDepthPass m_FinalDepthCopyPass;
#endif

    private Material m_BlitMaterial;
    private Material m_CopyDepthMaterial;

    private bool m_IsActiveTargetBackBuffer;

    public TinyRenderGraphRenderer(TinyRenderPipelineAsset asset)
    {
        if (asset.shaders != null)
        {
            m_BlitMaterial = CoreUtils.CreateEngineMaterial(asset.shaders.blitShader);
            m_CopyDepthMaterial = CoreUtils.CreateEngineMaterial(asset.shaders.copyDepthShader);
        }

        m_ForwardLights = new ForwardLights();
        m_MainLightShadowPass = new MainLightShadowPass();
        m_AdditionalLightsShadowPass = new AdditionalLightsShadowPass();

        m_RenderOpaqueForwardPass = new DrawObjectsForwardPass(true);
        m_CopyDepthPass = new CopyDepthPass(m_CopyDepthMaterial);
        m_DrawSkyboxPass = new DrawSkyboxPass();
        m_CopyColorPass = new CopyColorPass(m_BlitMaterial);
        m_RenderTransparentForwardPass = new DrawObjectsForwardPass();
        m_PostProcessingPass = new PostProcessingPass();
        m_FinalBlitPass = new FinalBlitPass(m_BlitMaterial);

#if UNITY_EDITOR
        m_FinalDepthCopyPass = new CopyDepthPass(m_CopyDepthMaterial, true);
#endif

        m_IsActiveTargetBackBuffer = true;
    }

    private void RecordRenderGraph(RenderGraph renderGraph, ref RenderingData renderingData)
    {
        var camera = renderingData.camera;
        var cameraType = camera.cameraType;

        // Setup lights data
        m_ForwardLights.SetupRenderGraphLights(renderGraph, ref renderingData);

        // Main light shadowmap
        if (m_MainLightShadowPass.Setup(ref renderingData))
        {
            m_MainLightShadowmapTextureHdl = m_MainLightShadowPass.RenderGraphRender(renderGraph, ref renderingData);
        }

        // Additional lights shadowmap
        if (m_AdditionalLightsShadowPass.Setup(ref renderingData))
        {
            m_AdditionalLightsShadowmapTextureHdl = m_AdditionalLightsShadowPass.RenderGraphRender(renderGraph, ref renderingData);
        }

        var additionalCameraData = camera.GetComponent<AdditionalCameraData>();
        var postProcessingData = additionalCameraData ?
            (additionalCameraData.isOverridePostProcessingData ? additionalCameraData.overridePostProcessingData : renderingData.postProcessingData) : renderingData.postProcessingData;

        bool applyPostProcessing = postProcessingData != null;
        // Only game camera and scene camera have post processing effects
        applyPostProcessing &= cameraType <= CameraType.SceneView;
        // Check if disable post processing effects in scene view
        applyPostProcessing &= CoreUtils.ArePostProcessesEnabled(camera);

        // Color grading generating LUT pass
        bool generateColorGradingLut = applyPostProcessing && renderingData.isHdrEnabled;
        if (generateColorGradingLut)
        {
            // TODO: m_ColorGradingLutPass
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

        m_RenderOpaqueForwardPass.DrawRenderGraphObjects(renderGraph, m_ActiveRenderGraphCameraColorHandle, m_ActiveRenderGraphCameraDepthHandle, m_MainLightShadowmapTextureHdl, m_AdditionalLightsShadowmapTextureHdl, ref renderingData);

        // Copy depth texture if needed after rendering opaque objects
        if (needCopyDepth)
        {
            var depthDescriptor = renderingData.cameraTargetDescriptor;
            depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
            depthDescriptor.depthStencilFormat = GraphicsFormat.None;
            depthDescriptor.depthBufferBits = (int)DepthBits.None;

            m_DepthTextureHdl = RenderingUtils.CreateRenderGraphTexture(renderGraph, depthDescriptor, "_CameraDepthTexture", true);
            m_CopyDepthPass.RenderGraphRender(renderGraph, m_ActiveRenderGraphCameraDepthHandle, m_DepthTextureHdl, ref renderingData, true);
        }
        else
        {
            RenderingUtils.SetGlobalRenderGraphTextureName(renderGraph, "_CameraDepthTexture", SystemInfo.usesReversedZBuffer ? renderGraph.defaultResources.blackTexture : renderGraph.defaultResources.whiteTexture, "SetDefaultGlobalCameraDepthTexture");
        }

        m_DrawSkyboxPass.DrawRenderGraphSkybox(renderGraph, m_ActiveRenderGraphCameraColorHandle, m_ActiveRenderGraphCameraDepthHandle, ref renderingData);

        // Copy color texture if needed after rendering skybox
        if (needCopyColor)
        {
            var colorDescriptor = renderingData.cameraTargetDescriptor;
            colorDescriptor.depthBufferBits = (int)DepthBits.None;

            m_OpaqueColorTextureHdl = RenderingUtils.CreateRenderGraphTexture(renderGraph, colorDescriptor, "_CameraOpaqueTexture", true, FilterMode.Bilinear);
            m_CopyColorPass.RenderGraphRender(renderGraph, m_ActiveRenderGraphCameraColorHandle, m_OpaqueColorTextureHdl, ref renderingData);
        }
        else
        {
            RenderingUtils.SetGlobalRenderGraphTextureName(renderGraph, "_CameraOpaqueTexture", renderGraph.defaultResources.whiteTexture);
        }

        m_RenderTransparentForwardPass.DrawRenderGraphObjects(renderGraph, m_ActiveRenderGraphCameraColorHandle, m_ActiveRenderGraphCameraDepthHandle, m_MainLightShadowmapTextureHdl, m_AdditionalLightsShadowmapTextureHdl, ref renderingData);

        DrawRenderGraphGizmos(renderGraph, m_ActiveRenderGraphCameraColorHandle, m_ActiveRenderGraphCameraDepthHandle, GizmoSubset.PreImageEffects, ref renderingData);

        // FXAA is enabled only if post-processing is enabled
        bool hasFxaaPass = applyPostProcessing && (postProcessingData.antialiasingMode == PostProcessingData.AntialiasingMode.FastApproximateAntialiasing);
        // Check post-processing pass need to resolve to final camera target
        bool resolvePostProcessingToCameraTarget = !hasFxaaPass;

        if (applyPostProcessing)
        {
            // TODO: m_PostProcessingPass
            var target = resolvePostProcessingToCameraTarget ? m_BackBufferColor : nextRenderGraphCameraColorHandle;
            m_PostProcessingPass.RenderGraphRender(renderGraph, in m_ActiveRenderGraphCameraColorHandle, TextureHandle.nullHandle, target, resolvePostProcessingToCameraTarget, postProcessingData, ref renderingData);

            if (resolvePostProcessingToCameraTarget)
            {
                m_ActiveRenderGraphCameraColorHandle = m_BackBufferColor;
                // m_ActiveRenderGraphCameraDepthHandle = m_BackBufferDepth;
            }
            else
            {
                m_ActiveRenderGraphCameraColorHandle = target;
            }
        }

        // FXAA pass always blit to final camera target
        if (hasFxaaPass)
        {
            // TODO: m_FXAAPass
        }

        // Check active color attachment is resolved to final target:
        // 1. FXAA pass is enabled, it always blit active color attachment to final camera target
        // 2. Post-processing is enabled and FXAA pass is disabled, active color attachment apply post-processing effects and then blit it to final camera target
        // 3. Active color attachment is the final camera target
        bool cameraTargetResolved = hasFxaaPass || (applyPostProcessing && resolvePostProcessingToCameraTarget) || m_IsActiveTargetBackBuffer;

        // If is not resolved to final camera target, need final blit pass to do this
        if (!cameraTargetResolved)
        {
            m_FinalBlitPass.RenderGraphRender(renderGraph, m_ActiveRenderGraphCameraColorHandle, m_BackBufferColor, ref renderingData);

            m_IsActiveTargetBackBuffer = true;
        }

        // When use intermediate rendering, copying depth to the camera target finally to make gizmos render correctly in scene camera view or preview camera view
#if UNITY_EDITOR
        bool isGizmosEnabled = Handles.ShouldRenderGizmos();
        bool isSceneViewOrPreviewCamera = cameraType == CameraType.SceneView || cameraType == CameraType.Preview;
        if (intermediateRenderTexture && (isSceneViewOrPreviewCamera || isGizmosEnabled))
        {
            m_FinalDepthCopyPass.RenderGraphRender(renderGraph, m_ActiveRenderGraphCameraDepthHandle, m_BackBufferDepth, ref renderingData, false, "FinalDepthCopy");
        }
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
        public TinyBaseRenderer renderer;
        public TextureHandle colorTarget;
        public RenderingData renderingData;
    }

    private void SetupRenderGraphCameraProperties(RenderGraph renderGraph, ref RenderingData renderingData, TextureHandle colorTarget)
    {
        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_SetupCameraPropertiesSampler.name, out var passData, s_SetupCameraPropertiesSampler))
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

        var executor = renderGraph.RecordAndExecute(renderGraphParameters);
        RecordRenderGraph(renderGraph, ref renderingData);
        executor.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        m_PostProcessingPass?.Dispose();

        m_CameraColorHandles[0]?.Release();
        m_CameraColorHandles[1]?.Release();
        m_CameraDepthHandle?.Release();

        CoreUtils.Destroy(m_BlitMaterial);
        CoreUtils.Destroy(m_CopyDepthMaterial);
    }
}
