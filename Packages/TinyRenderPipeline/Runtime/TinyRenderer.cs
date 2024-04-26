#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public partial class TinyRenderer
{
    private const GraphicsFormat k_DepthStencilFormat = GraphicsFormat.D32_SFloat_S8_UInt;
    private const int k_DepthBufferBits = 32;

    private static class Profiling
    {
        public static readonly ProfilingSampler drawOpaque = new ($"{nameof(DrawOpaque)}");
        public static readonly ProfilingSampler drawTransparent = new ($"{nameof(DrawTransparent)}");
        public static readonly ProfilingSampler drawGizmos = new ($"{nameof(DrawGizmos)}");
    }

    private ForwardLights m_ForwardLights;
    private MainLightShadowPass m_MainLightShadowPass;
    private AdditionalLightsShadowPass m_AdditionalLightsShadowPass;
    private PostProcessingPass m_PostProcessingPass;
    private ColorGradingLutPass m_ColorGradingLutPass;
    private FinalBlitPass m_FinalBlitPass;
    private FXAAPass m_FXAAPass;

    private CopyDepthPass m_CopyDepthPass;
    private CopyColorPass m_CopyColorPass;

#if UNITY_EDITOR
    private CopyDepthPass m_FinalDepthCopyPass;
#endif

    private RenderTargetBufferSystem m_ColorBufferSystem;

    private RTHandle m_ActiveCameraColorAttachment;
    private RTHandle m_ActiveCameraDepthAttachment;
    private RTHandle m_CameraDepthAttachment;
    private RTHandle m_TargetColorHandle;
    private RTHandle m_TargetDepthHandle;

    private RTHandle m_DepthTexture;
    private RTHandle m_OpaqueColor;

    private Material m_BlitMaterial;
    private Material m_CopyDepthMaterial;

    public TinyRenderer(TinyRenderPipelineAsset asset)
    {
        if (asset.shaders != null)
        {
            m_BlitMaterial = CoreUtils.CreateEngineMaterial(asset.shaders.blitShader);
            m_CopyDepthMaterial = CoreUtils.CreateEngineMaterial(asset.shaders.copyDepthShader);
        }

        m_ForwardLights = new ForwardLights();
        m_MainLightShadowPass = new MainLightShadowPass();
        m_AdditionalLightsShadowPass = new AdditionalLightsShadowPass();
        m_PostProcessingPass = new PostProcessingPass();
        m_ColorGradingLutPass = new ColorGradingLutPass();
        m_FinalBlitPass = new FinalBlitPass(m_BlitMaterial);
        m_CopyDepthPass = new CopyDepthPass(m_CopyDepthMaterial);
        m_CopyColorPass = new CopyColorPass(m_BlitMaterial);
        m_FXAAPass = new FXAAPass();

#if UNITY_EDITOR
        m_FinalDepthCopyPass = new CopyDepthPass(m_CopyDepthMaterial);
#endif

        m_ColorBufferSystem = new RenderTargetBufferSystem("_CameraColorAttachment");
    }

    public void Execute(ref RenderingData renderingData)
    {
        var context = renderingData.renderContext;
        var camera = renderingData.camera;
        var cameraType = camera.cameraType;

        var cmd = renderingData.commandBuffer;

        // Setup lighting data
        m_ForwardLights.Setup(context, ref renderingData);

        // Render main light shadowmap
        if (m_MainLightShadowPass.Setup(ref renderingData))
        {
            m_MainLightShadowPass.ExecutePass(context, ref renderingData);
        }

        // Render additional lights shadowmap
        if (m_AdditionalLightsShadowPass.Setup(ref renderingData))
        {
            m_AdditionalLightsShadowPass.ExecutePass(context, ref renderingData);
        }

        var cameraTargetDescriptor = renderingData.cameraTargetDescriptor;
        var colorDescriptor = cameraTargetDescriptor;
        colorDescriptor.useMipMap = false;
        colorDescriptor.autoGenerateMips = false;
        colorDescriptor.depthBufferBits = (int)DepthBits.None;
        m_ColorBufferSystem.SetCameraSettings(colorDescriptor, FilterMode.Bilinear);

        RenderTargetIdentifier targetId = BuiltinRenderTextureType.CameraTarget;
        if (m_TargetColorHandle == null)
        {
            m_TargetColorHandle = RTHandles.Alloc(targetId);
        }
        else if (m_TargetColorHandle.nameID != targetId)
        {
            RTHandleStaticHelpers.SetRTHandleUserManagedWrapper(ref m_TargetColorHandle, targetId);
        }

        if (m_TargetDepthHandle == null)
        {
            m_TargetDepthHandle = RTHandles.Alloc(targetId);
        }
        else if (m_TargetDepthHandle.nameID != targetId)
        {
            RTHandleStaticHelpers.SetRTHandleUserManagedWrapper(ref m_TargetDepthHandle, targetId);
        }

        // Setup camera properties
        context.SetupCameraProperties(camera);

        // Post processing
        // Check post processing data setup
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
            int lutHeight = renderingData.lutSize;
            int lutWidth = lutHeight * lutHeight;
            var lutFormat = renderingData.isHdrEnabled ? SystemInfo.GetGraphicsFormat(DefaultFormat.HDR) : SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);
            var descriptor = new RenderTextureDescriptor(lutWidth, lutHeight, lutFormat, 0);

            RenderingUtils.ReAllocateIfNeeded(ref m_ColorGradingLutPass.m_ColorGradingLut, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_InternalGradingLut");

            m_ColorGradingLutPass.Setup(postProcessingData);
            m_ColorGradingLutPass.ExecutePass(context, ref renderingData);
        }

        // Create color texture logic
        bool needCopyColor = renderingData.copyColorTexture;
        if (additionalCameraData)
            needCopyColor &= additionalCameraData.requireColorTexture;

        bool createColorTexture = applyPostProcessing; // post-processing is active
        createColorTexture |= !renderingData.isDefaultCameraViewport; // camera's viewport rect is not default(0, 0, 1, 1)
        createColorTexture |= needCopyColor; // copy color texture is enabled

        // Check if need copy depth texture
        bool needCopyDepth = renderingData.copyDepthTexture;
        if (additionalCameraData)
            needCopyDepth &= additionalCameraData.requireDepthTexture;

        // Use intermediate rendering textures while:
        // 1. need create color texture
        // 2. need copy depth texture
        // 3. render scale is not 1.0
        bool useRenderScale = renderingData.renderScale < 1.0f || renderingData.renderScale > 1.0f;
        bool intermediateRenderTexture = createColorTexture || needCopyDepth || useRenderScale;

        // Create color buffer and depth buffer for intermediate rendering
        if (intermediateRenderTexture)
            CreateCameraRenderTarget(context, ref cameraTargetDescriptor, cmd, camera);

        m_ActiveCameraColorAttachment = intermediateRenderTexture ? m_ColorBufferSystem.PeekBackBuffer() : m_TargetColorHandle;
        m_ActiveCameraDepthAttachment = intermediateRenderTexture ? m_CameraDepthAttachment : m_TargetDepthHandle;

        // Setup render target
        cmd.SetRenderTarget(m_ActiveCameraColorAttachment, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
            m_ActiveCameraDepthAttachment, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);

        // Setup clear flags
        CameraClearFlags clearFlags = camera.clearFlags;
        if (intermediateRenderTexture)
        {
            if (clearFlags > CameraClearFlags.Color)
                clearFlags = CameraClearFlags.Color;
        }

        var cameraBackgroundColorSRGB = camera.backgroundColor;
#if UNITY_EDITOR
        if (cameraType == CameraType.Preview)
            cameraBackgroundColorSRGB = new Color(82f / 255.0f, 82f / 255.0f, 82.0f / 255.0f, 0.0f);
#endif
        cmd.ClearRenderTarget(clearFlags <= CameraClearFlags.Depth, clearFlags <= CameraClearFlags.Color,
            clearFlags == CameraClearFlags.Color ? cameraBackgroundColorSRGB.linear : Color.clear);

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        // Opaque objects
        DrawOpaque(context, ref renderingData);

        // Copy depth texture if needed after rendering opaque objects
        if (needCopyDepth)
        {
            var depthDescriptor = cameraTargetDescriptor;
            depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
            depthDescriptor.depthStencilFormat = GraphicsFormat.None;
            depthDescriptor.depthBufferBits = 0;

            depthDescriptor.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref m_DepthTexture, depthDescriptor, FilterMode.Point, wrapMode: TextureWrapMode.Clamp, name: "_CameraDepthTexture");
            cmd.SetGlobalTexture(m_DepthTexture.name, m_DepthTexture.nameID);

            m_CopyDepthPass.Setup(m_ActiveCameraDepthAttachment, m_DepthTexture);
            m_CopyDepthPass.ExecutePass(context, ref renderingData);

            // Switch back to active render targets after coping depth
            cmd.SetRenderTarget(m_ActiveCameraColorAttachment, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                m_ActiveCameraDepthAttachment, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
        else
        {
            Shader.SetGlobalTexture("_CameraDepthTexture", SystemInfo.usesReversedZBuffer ? Texture2D.blackTexture : Texture2D.whiteTexture);
        }

        // Skybox
        DrawSkybox(context, camera);

        // Copy color texture if needed after rendering skybox
        if (needCopyColor)
        {
            var descriptor = cameraTargetDescriptor;
            descriptor.msaaSamples = 1;
            descriptor.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref m_OpaqueColor, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_CameraOpaqueTexture");
            cmd.SetGlobalTexture(m_OpaqueColor.name, m_OpaqueColor.nameID);

            m_CopyColorPass.Setup(m_ActiveCameraColorAttachment, m_OpaqueColor);
            m_CopyColorPass.ExecutePass(context, ref renderingData);

            // Switch back to active render targets after coping depth
            cmd.SetRenderTarget(m_ActiveCameraColorAttachment, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                m_ActiveCameraDepthAttachment, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
        else
        {
            Shader.SetGlobalTexture("_CameraOpaqueTexture", Texture2D.grayTexture);
        }

        // Transparent objects
        DrawTransparent(context, ref renderingData);

        DrawGizmos(context, cmd, camera, GizmoSubset.PreImageEffects);

        // FXAA is enabled only if post-processing is enabled
        bool hasFXAAPass = applyPostProcessing && (postProcessingData.antialiasingMode == PostProcessingData.AntialiasingMode.FastApproximateAntialiasing);
        // Check post-processing pass need to resolve to final camera target
        bool resolvePostProcessingToCameraTarget = !hasFXAAPass;

        if (applyPostProcessing)
        {
            m_PostProcessingPass.Setup(cameraTargetDescriptor, m_ActiveCameraColorAttachment, resolvePostProcessingToCameraTarget, m_ColorGradingLutPass.m_ColorGradingLut, postProcessingData);
            m_PostProcessingPass.ExecutePass(context, ref renderingData);
        }

        // FXAA pass always blit to final camera target
        if (hasFXAAPass)
        {
            m_FXAAPass.Setup(m_ActiveCameraColorAttachment, postProcessingData);
            m_FXAAPass.ExecutePass(context, ref renderingData);
        }

        // Check active color attachment is resolved to final target:
        // 1. FXAA pass is enabled, it always blit active color attachment to final camera target
        // 2. Post-processing is enabled and FXAA pass is disabled, active color attachment apply post-processing effects and then blit it to final camera target
        // 3. Active color attachment is the final camera target
        bool cameraTargetResolved = hasFXAAPass || (applyPostProcessing && resolvePostProcessingToCameraTarget) || m_ActiveCameraColorAttachment.nameID == m_TargetColorHandle.nameID;

        // If is not resolved to final camera target, need final blit pass to do this
        if (!cameraTargetResolved)
        {
            m_FinalBlitPass.Setup(m_ActiveCameraColorAttachment);
            m_FinalBlitPass.ExecutePass(context, ref renderingData);
        }

        // Blit depth buffer to camera target for Gizmos rendering in scene view and game view while using intermediate rendering
        bool isGizmosEnabled = false;
#if UNITY_EDITOR
        isGizmosEnabled = Handles.ShouldRenderGizmos();
        bool isSceneViewOrPreviewCamera = cameraType == CameraType.SceneView || cameraType == CameraType.Preview;
        if (intermediateRenderTexture && (isSceneViewOrPreviewCamera || isGizmosEnabled))
        {
            m_FinalDepthCopyPass.Setup(m_ActiveCameraDepthAttachment, TinyRenderPipeline.k_CameraTarget, copyToDepthTexture: true);
            m_FinalDepthCopyPass.ExecutePass(context, ref renderingData);
        }
#endif

        DrawGizmos(context, cmd, camera, GizmoSubset.PostImageEffects);

        // Finish rendering
        m_ColorBufferSystem.Clear();
        m_ActiveCameraColorAttachment = null;
        m_ActiveCameraDepthAttachment = null;
    }

    public void Dispose(bool disposing)
    {
        m_ColorGradingLutPass?.Dispose();

        m_PostProcessingPass?.Dispose();

        m_ColorBufferSystem.Dispose();

        m_MainLightShadowPass?.Dispose();
        m_AdditionalLightsShadowPass?.Dispose();

        m_FinalBlitPass?.Dispose();

        m_FXAAPass?.Dispose();

        m_CameraDepthAttachment?.Release();

        m_TargetColorHandle?.Release();
        m_TargetDepthHandle?.Release();

        m_DepthTexture?.Release();
        m_OpaqueColor?.Release();

        CoreUtils.Destroy(m_BlitMaterial);
        CoreUtils.Destroy(m_CopyDepthMaterial);
    }

    private void DrawGizmos(ScriptableRenderContext context, CommandBuffer cmd, Camera camera, GizmoSubset gizmoSubset)
    {
#if UNITY_EDITOR
        if (!Handles.ShouldRenderGizmos())
            return;

        using (new ProfilingScope(cmd, Profiling.drawGizmos))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            context.DrawGizmos(camera, gizmoSubset);
        }

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
#endif
    }

    private void CreateCameraRenderTarget(ScriptableRenderContext context, ref RenderTextureDescriptor descriptor, CommandBuffer cmd, Camera camera)
    {
        if (m_ColorBufferSystem.PeekBackBuffer() == null || m_ColorBufferSystem.PeekBackBuffer().nameID != BuiltinRenderTextureType.CameraTarget)
        {
            m_ActiveCameraColorAttachment = m_ColorBufferSystem.GetBackBuffer(cmd);
        }

        if (m_CameraDepthAttachment == null || m_CameraDepthAttachment.nameID != BuiltinRenderTextureType.CameraTarget)
        {
            var depthDescriptor = descriptor;
            depthDescriptor.useMipMap = false;
            depthDescriptor.autoGenerateMips = false;
            depthDescriptor.bindMS = false;

            depthDescriptor.graphicsFormat = GraphicsFormat.None;
            depthDescriptor.depthStencilFormat = k_DepthStencilFormat;
            depthDescriptor.depthBufferBits = k_DepthBufferBits;
            RenderingUtils.ReAllocateIfNeeded(ref m_CameraDepthAttachment, depthDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_CameraDepthAttachment");
            cmd.SetGlobalTexture(m_CameraDepthAttachment.name, m_CameraDepthAttachment.nameID);
        }

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
    }

    public RTHandle GetCameraColorFrontBuffer(CommandBuffer cmd)
    {
        return m_ColorBufferSystem.GetFrontBuffer(cmd);
    }

    public void SwapColorBuffer(CommandBuffer cmd)
    {
        m_ColorBufferSystem.Swap();

        m_ActiveCameraColorAttachment = m_ColorBufferSystem.GetBackBuffer(cmd);
    }
}
