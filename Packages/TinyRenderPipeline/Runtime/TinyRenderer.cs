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
        public static readonly ProfilingSampler drawOpaque = new ProfilingSampler($"{nameof(DrawOpaque)}");
        public static readonly ProfilingSampler drawTransparent = new ProfilingSampler($"{nameof(DrawTransparent)}");
        public static readonly ProfilingSampler drawGizmos = new ProfilingSampler($"{nameof(DrawGizmos)}");
    }

    private static readonly RTHandle k_CameraTarget = RTHandles.Alloc(BuiltinRenderTextureType.CameraTarget);

    private ForwardLights m_ForwardLights;
    private MainLightShadowPass m_MainLightShadowPass;
    private AdditionalLightsShadowPass m_AdditionalLightsShadowPass;
    private PostProcessingPass m_PostProcessingPass;
    private ColorGradingLutPass m_ColorGradingLutPass;
    private FinalBlitPass m_FinalBlitPass;

    private CopyDepthPass m_CopyDepthPass;

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

#if UNITY_EDITOR
        m_FinalDepthCopyPass = new CopyDepthPass(m_CopyDepthMaterial);
#endif

        m_ColorBufferSystem = new RenderTargetBufferSystem("_CameraColorAttachment");
    }

    public void Execute(ref RenderingData renderingData)
    {
        var context = renderingData.renderContext;
        var camera = renderingData.camera;
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
        var postProcessingData = additionalCameraData ? (additionalCameraData.isOverridePostProcessingData ? additionalCameraData.overridePostProcessingData : renderingData.postProcessingData) : renderingData.postProcessingData;
        bool applyPostProcessing = postProcessingData != null;
        // Only game camera and scene camera have post processing effects
        applyPostProcessing &= camera.cameraType <= CameraType.SceneView;
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

        // Check if need to create color buffer:
        // 1. Post-processing is active
        // 2. Camera's viewport rect is not default({0, 0, 1, 1})
        bool createColorTexture = applyPostProcessing;
        createColorTexture |= !renderingData.isDefaultCameraViewport;

        // Check if need copy depth texture
        bool needCopyDepth = renderingData.copyDepthTexture;
        if (additionalCameraData)
            needCopyDepth &= additionalCameraData.requireDepthTexture;

        // Use intermediate rendering textures while:
        // 1. need create color texture || 2. need copy depth texture
        bool intermediateRenderTexture = createColorTexture || needCopyDepth;

        // Create color buffer and depth buffer for intermediate rendering
        if (intermediateRenderTexture)
            CreateCameraRenderTarget(context, ref cameraTargetDescriptor, cmd, camera);

        if (needCopyDepth)
        {
            var depthDescriptor = cameraTargetDescriptor;
            depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
            depthDescriptor.depthStencilFormat = GraphicsFormat.None;
            depthDescriptor.depthBufferBits = 0;

            depthDescriptor.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref m_DepthTexture, depthDescriptor, FilterMode.Point, wrapMode: TextureWrapMode.Clamp, name: "_CameraDepthTexture");

            cmd.SetGlobalTexture(m_DepthTexture.name, m_DepthTexture.nameID);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

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
        if (camera.cameraType == CameraType.Preview)
            cameraBackgroundColorSRGB = new Color(82f / 255.0f, 82f / 255.0f, 82.0f / 255.0f, 0.0f);
#endif
        cmd.ClearRenderTarget(clearFlags <= CameraClearFlags.Depth, clearFlags <= CameraClearFlags.Color,
            clearFlags == CameraClearFlags.Color ? cameraBackgroundColorSRGB.linear : Color.clear);

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        // Opaque objects
        DrawOpaque(context, ref renderingData);

        // Skybox
        DrawSkybox(context, camera);

        // Copy depth texture if needed
        if (needCopyDepth)
        {
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

        // Transparent objects
        DrawTransparent(context, ref renderingData);

        DrawGizmos(context, cmd, camera, GizmoSubset.PreImageEffects);

        // If post-processing effect is enabled: first applying post-processing effects, and then blit to final camera target
        if (applyPostProcessing)
        {
            m_PostProcessingPass.Setup(cameraTargetDescriptor, m_ActiveCameraColorAttachment, true, m_ColorGradingLutPass.m_ColorGradingLut, postProcessingData);
            m_PostProcessingPass.ExecutePass(context, ref renderingData);
        }
        // If post-processing effect is disabled and intermediate rendering is in use: just blit to final camera target
        else if (intermediateRenderTexture)
        {
            m_FinalBlitPass.Setup(m_ActiveCameraColorAttachment);
            m_FinalBlitPass.ExecutePass(context, ref renderingData);
        }

        // Blit depth buffer to camera target for Gizmos rendering in scene view and game view while using intermediate rendering
        bool isGizmosEnabled = false;
#if UNITY_EDITOR
        isGizmosEnabled = Handles.ShouldRenderGizmos();
        var cameraType = camera.cameraType;
        bool isSceneViewOrPreviewCamera = cameraType == CameraType.SceneView || cameraType == CameraType.Preview;
        if (intermediateRenderTexture && (isSceneViewOrPreviewCamera || isGizmosEnabled))
        {
            m_FinalDepthCopyPass.Setup(m_ActiveCameraDepthAttachment, k_CameraTarget, copyToDepthTexture: true);
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

        m_CameraDepthAttachment?.Release();

        m_TargetColorHandle?.Release();
        m_TargetDepthHandle?.Release();

        m_DepthTexture?.Release();

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
}
