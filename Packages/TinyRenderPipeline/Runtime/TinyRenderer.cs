#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public partial class TinyRenderer
{
    private static class Profiling
    {
        public static readonly ProfilingSampler drawOpaque = new ProfilingSampler($"{nameof(DrawOpaque)}");
        public static readonly ProfilingSampler drawTransparent = new ProfilingSampler($"{nameof(DrawTransparent)}");
        public static readonly ProfilingSampler drawGizmos = new ProfilingSampler($"{nameof(DrawGizmos)}");
    }

    private ForwardLights m_ForwardLights;
    private MainLightShadowPass m_MainLightShadowPass;
    private AdditionalLightsShadowPass m_AdditionalLightsShadowPass;
    private PostProcessingPass m_PostProcessingPass;

    private ColorGradingLutPass m_ColorGradingLutPass;

    private RenderTargetBufferSystem m_ColorBufferSystem;

    private RTHandle m_ActiveCameraColorAttachment;
    private RTHandle m_ActiveCameraDepthAttachment;
    private RTHandle m_CameraDepthAttachment;
    private RTHandle m_TargetColorHandle;
    private RTHandle m_TargetDepthHandle;

    public TinyRenderer(PostProcessingData postProcessingData)
    {
        m_ForwardLights = new ForwardLights();
        m_MainLightShadowPass = new MainLightShadowPass();
        m_AdditionalLightsShadowPass = new AdditionalLightsShadowPass();
        m_PostProcessingPass = new PostProcessingPass(postProcessingData);
        m_ColorGradingLutPass = new ColorGradingLutPass(postProcessingData);

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
        bool applyPostProcessingEffects = m_PostProcessingPass.IsValid();
        // Only game camera and scene camera have post processing effects
        applyPostProcessingEffects &= camera.cameraType <= CameraType.SceneView;
        // Check if disable post processing effects in scene view
        applyPostProcessingEffects &= CoreUtils.ArePostProcessesEnabled(camera);

        // Color grading generating LUT pass
        bool generateColorGradingLut = applyPostProcessingEffects && renderingData.isHdrEnabled;
        if (generateColorGradingLut)
        {
            int lutHeight = renderingData.lutSize;
            int lutWidth = lutHeight * lutHeight;
            var lutFormat = renderingData.isHdrEnabled ? SystemInfo.GetGraphicsFormat(DefaultFormat.HDR) : SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);
            var descriptor = new RenderTextureDescriptor(lutWidth, lutHeight, lutFormat, 0);

            RenderingUtils.ReAllocateIfNeeded(ref m_ColorGradingLutPass.m_ColorGradingLut, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_InternalGradingLut");

            m_ColorGradingLutPass.ExecutePass(context, ref renderingData);
        }

        if (applyPostProcessingEffects)
            CreateCameraRenderTarget(context, ref cameraTargetDescriptor, cmd, camera);

        m_ActiveCameraColorAttachment = applyPostProcessingEffects ? m_ColorBufferSystem.PeekBackBuffer() : m_TargetColorHandle;
        m_ActiveCameraDepthAttachment = applyPostProcessingEffects ? m_CameraDepthAttachment : m_TargetDepthHandle;

        // Setup render target
        cmd.SetRenderTarget(m_ActiveCameraColorAttachment, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
            m_ActiveCameraDepthAttachment, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);

        // Setup clear flags
        CameraClearFlags clearFlags = camera.clearFlags;
        if (applyPostProcessingEffects)
        {
            if (clearFlags > CameraClearFlags.Color)
                clearFlags = CameraClearFlags.Color;
        }
        cmd.ClearRenderTarget(clearFlags <= CameraClearFlags.Depth, clearFlags <= CameraClearFlags.Color,
            clearFlags == CameraClearFlags.Color ? camera.backgroundColor.linear : Color.clear);

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        // Start rendering
        DrawOpaque(context, ref renderingData);

        DrawSkybox(context, camera);

        DrawTransparent(context, ref renderingData);

        DrawGizmos(context, cmd, camera, GizmoSubset.PreImageEffects);

        if (applyPostProcessingEffects)
        {
            m_PostProcessingPass.Setup(cameraTargetDescriptor, m_ActiveCameraColorAttachment, true, m_ColorGradingLutPass.m_ColorGradingLut);
            m_PostProcessingPass.ExecutePass(context, ref renderingData);
        }

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

        m_CameraDepthAttachment?.Release();

        m_TargetColorHandle?.Release();
        m_TargetDepthHandle?.Release();
    }

    private void DrawGizmos(ScriptableRenderContext context, CommandBuffer cmd, Camera camera, GizmoSubset gizmoSubset)
    {
#if UNITY_EDITOR
        if (!Handles.ShouldRenderGizmos() || camera.sceneViewFilterMode == Camera.SceneViewFilterMode.ShowFiltered)
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
            depthDescriptor.depthStencilFormat = GraphicsFormat.D32_SFloat_S8_UInt;
            RenderingUtils.ReAllocateIfNeeded(ref m_CameraDepthAttachment, depthDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_CameraDepthAttachment");
        }

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
    }
}
