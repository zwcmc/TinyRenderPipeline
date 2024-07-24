using UnityEditor;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public class TinyRenderer : TinyBaseRenderer
{
    private const GraphicsFormat k_DepthStencilFormat = GraphicsFormat.D32_SFloat_S8_UInt;
    private const int k_DepthBufferBits = 32;

#if UNITY_EDITOR
    private static readonly ProfilingSampler s_DrawGizmosPassSampler = new ProfilingSampler("DrawGizmosPass");
#endif

    private RenderTargetBufferSystem m_ColorBufferSystem;

    private RTHandle m_ActiveCameraColorAttachment;
    private RTHandle m_ActiveCameraDepthAttachment;
    private RTHandle m_CameraDepthAttachment;
    private RTHandle m_TargetColorHandle;
    private RTHandle m_TargetDepthHandle;

    private RTHandle m_DepthTexture;
    private RTHandle m_OpaqueColor;

    private RTHandle m_ColorGradingLut;

    public TinyRenderer(TinyRenderPipelineAsset asset) : base(asset)
    {
        m_ColorBufferSystem = new RenderTargetBufferSystem("_CameraColorAttachment");
    }

    public void Execute(ref RenderingData renderingData)
    {
        var context = renderingData.renderContext;
        var camera = renderingData.camera;
        var cameraType = camera.cameraType;

        var cmd = renderingData.commandBuffer;

        // PreSetup for forward+ rendering path
        forwardLights.PreSetup(ref renderingData);

        // Setup lighting data
        forwardLights.SetupLights(cmd, ref renderingData);

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        // Render main light shadowmap
        if (mainLightShadowPass.Setup(ref renderingData))
        {
            mainLightShadowPass.Render(context, ref renderingData);
        }

        // Render additional lights shadowmap
        if (additionalLightsShadowPass.Setup(ref renderingData))
        {
            additionalLightsShadowPass.Render(context, ref renderingData);
        }

        var cameraTargetDescriptor = renderingData.cameraTargetDescriptor;
        var colorDescriptor = cameraTargetDescriptor;
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
            int lutHeight = renderingData.lutSize;
            int lutWidth = lutHeight * lutHeight;
            var lutFormat = renderingData.defaultFormat;
            var descriptor = new RenderTextureDescriptor(lutWidth, lutHeight, lutFormat, 0);
            RenderingUtils.ReAllocateIfNeeded(ref m_ColorGradingLut, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_InternalGradingLut");

            colorGradingLutPass.Render(context, in m_ColorGradingLut, postProcessingData, ref renderingData);
        }

        // Create color texture logic
        bool needCopyColor = renderingData.copyColorTexture;
        if (additionalCameraData)
        {
            needCopyColor &= additionalCameraData.requireColorTexture;
        }

        bool createColorTexture = applyPostProcessing; // post-processing is active
        createColorTexture |= !renderingData.isDefaultCameraViewport; // camera's viewport rect is not default(0, 0, 1, 1)
        createColorTexture |= needCopyColor; // copy color texture is enabled

        // Check if need copy depth texture
        bool needCopyDepth = renderingData.copyDepthTexture;
        if (additionalCameraData)
        {
            needCopyDepth &= additionalCameraData.requireDepthTexture;
        }

        // Use intermediate rendering textures while:
        // 1. need create color texture
        // 2. need copy depth texture
        // 3. render scale is not 1.0
        bool useRenderScale = renderingData.renderScale < 1.0f || renderingData.renderScale > 1.0f;
        bool intermediateRenderTexture = createColorTexture || needCopyDepth || useRenderScale;

        // Create color buffer and depth buffer for intermediate rendering
        if (intermediateRenderTexture)
        {
            CreateCameraRenderTarget(context, ref cameraTargetDescriptor, cmd, camera);
        }

        m_ActiveCameraColorAttachment = intermediateRenderTexture ? m_ColorBufferSystem.PeekBackBuffer() : m_TargetColorHandle;
        m_ActiveCameraDepthAttachment = intermediateRenderTexture ? m_CameraDepthAttachment : m_TargetDepthHandle;

        // Setup camera properties
        context.SetupCameraProperties(camera);
        // This is still required because of the following reasons:
        // - Camera billboard properties.
        // - Camera frustum planes: unity_CameraWorldClipPlanes[6]
        // - _ProjectionParams.x logic is deep inside GfxDevice
        SetPerCameraShaderVariables(CommandBufferHelpers.GetRasterCommandBuffer(cmd), camera, RenderingUtils.IsHandleYFlipped(m_ActiveCameraColorAttachment, camera));

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
            cameraBackgroundColorSRGB = new Color(82.0f / 255.0f, 82.0f / 255.0f, 82.0f / 255.0f, 0.0f);
#endif
        cmd.ClearRenderTarget(clearFlags <= CameraClearFlags.Depth, clearFlags <= CameraClearFlags.Color,
            clearFlags == CameraClearFlags.Color ? cameraBackgroundColorSRGB.linear : Color.clear);

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        // Opaque objects
        renderOpaqueForwardPass.Render(context, ref renderingData);

        // Copy depth texture if needed after rendering opaque objects
        if (needCopyDepth)
        {
            var depthDescriptor = cameraTargetDescriptor;
            depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
            depthDescriptor.depthStencilFormat = GraphicsFormat.None;
            depthDescriptor.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref m_DepthTexture, depthDescriptor, FilterMode.Point, wrapMode: TextureWrapMode.Clamp, name: "_CameraDepthTexture");
            cmd.SetGlobalTexture(m_DepthTexture.name, m_DepthTexture.nameID);

            copyDepthPass.Render(context, m_ActiveCameraDepthAttachment, m_DepthTexture, ref renderingData);

            // After copying the depth buffer, switching back to active render targets, and continuing to render skybox and transparent objects
            cmd.SetRenderTarget(m_ActiveCameraColorAttachment, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
                m_ActiveCameraDepthAttachment, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
        else
        {
            Shader.SetGlobalTexture("_CameraDepthTexture", SystemInfo.usesReversedZBuffer ? Texture2D.blackTexture : Texture2D.whiteTexture);
        }

        // Skybox
        renderSkyboxPass.Render(context, ref renderingData);

        // Copy color texture if needed after rendering skybox
        if (needCopyColor)
        {
            var descriptor = cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            RenderingUtils.ReAllocateIfNeeded(ref m_OpaqueColor, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_CameraOpaqueTexture");
            cmd.SetGlobalTexture(m_OpaqueColor.name, m_OpaqueColor.nameID);

            copyColorPass.Render(context, m_ActiveCameraColorAttachment, m_OpaqueColor, ref renderingData);

            // After copying the color buffer, switching back to active render targets, and continuing to render transparent objects
            cmd.SetRenderTarget(m_ActiveCameraColorAttachment, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
                m_ActiveCameraDepthAttachment, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
        else
        {
            Shader.SetGlobalTexture("_CameraOpaqueTexture", Texture2D.grayTexture);
        }

        // Transparent objects
        renderTransparentForwardPass.Render(context, ref renderingData);

        DrawGizmos(context, cmd, camera, GizmoSubset.PreImageEffects);

        // FXAA is enabled only if post-processing is enabled
        bool hasFxaaPass = applyPostProcessing && (postProcessingData.antialiasingMode == PostProcessingData.AntialiasingMode.FastApproximateAntialiasing);
        // Check post-processing pass need to resolve to final camera target
        bool resolvePostProcessingToCameraTarget = !hasFxaaPass;

        if (applyPostProcessing)
        {
            postProcessingPass.Render(context, m_ActiveCameraColorAttachment, resolvePostProcessingToCameraTarget, m_ColorGradingLut, postProcessingData, ref renderingData);
        }

        // FXAA pass always blit to final camera target
        if (hasFxaaPass)
        {
            fxaaPass.Render(context, m_ActiveCameraColorAttachment, postProcessingData, ref renderingData);
        }

        // Check active color attachment is resolved to final target:
        // 1. FXAA pass is enabled, it always blit active color attachment to final camera target
        // 2. Post-processing is enabled and FXAA pass is disabled, active color attachment apply post-processing effects and then blit it to final camera target
        // 3. Active color attachment is the final camera target
        bool cameraTargetResolved = hasFxaaPass || (applyPostProcessing && resolvePostProcessingToCameraTarget) || m_ActiveCameraColorAttachment.nameID == m_TargetColorHandle.nameID;

        // If is not resolved to final camera target, need final blit pass to do this
        if (!cameraTargetResolved)
        {
            finalBlitPass.Render(context, m_ActiveCameraColorAttachment, ref renderingData);
        }

        // When use intermediate rendering, copying depth to the camera target finally to make gizmos render correctly in scene camera view or preview camera view
#if UNITY_EDITOR
        bool isGizmosEnabled = Handles.ShouldRenderGizmos();
        bool isSceneViewOrPreviewCamera = cameraType == CameraType.SceneView || cameraType == CameraType.Preview;
        if (intermediateRenderTexture && (isSceneViewOrPreviewCamera || isGizmosEnabled))
        {
            finalDepthCopyPass.Render(context, m_ActiveCameraDepthAttachment, TinyRenderPipeline.k_CameraTarget, ref renderingData);
        }
#endif

        DrawGizmos(context, cmd, camera, GizmoSubset.PostImageEffects);

        // Finish rendering
        m_ColorBufferSystem.Clear();
        m_ActiveCameraColorAttachment = null;
        m_ActiveCameraDepthAttachment = null;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        m_ColorBufferSystem.Dispose();

        m_CameraDepthAttachment?.Release();

        m_TargetColorHandle?.Release();
        m_TargetDepthHandle?.Release();

        m_DepthTexture?.Release();
        m_OpaqueColor?.Release();

        m_ColorGradingLut?.Release();
    }

    [Conditional("UNITY_EDITOR")]
    private void DrawGizmos(ScriptableRenderContext context, CommandBuffer cmd, Camera camera, GizmoSubset gizmoSubset)
    {
#if UNITY_EDITOR
        if (!Handles.ShouldRenderGizmos())
            return;

        using (new ProfilingScope(cmd, s_DrawGizmosPassSampler))
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
