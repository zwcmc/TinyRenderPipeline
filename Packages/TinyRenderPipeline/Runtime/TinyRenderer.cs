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

    private RenderTargetBufferSystem m_ColorBufferSystem;

    private RTHandle m_ActiveCameraColorAttachment;
    private RTHandle m_ActiveCameraDepthAttachment;
    private RTHandle m_CameraDepthAttachment;
    private RTHandle m_TargetColorHandle;
    private RTHandle m_TargetDepthHandle;

    public TinyRenderer()
    {
        m_ForwardLights = new ForwardLights();
        m_MainLightShadowPass = new MainLightShadowPass();
        m_AdditionalLightsShadowPass = new AdditionalLightsShadowPass();
        m_PostProcessingPass = new PostProcessingPass(TinyRenderPipeline.asset);

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
            m_MainLightShadowPass.Render(context, ref renderingData);
        }

        // Render additional lights shadowmap
        if (m_AdditionalLightsShadowPass.Setup(ref renderingData))
        {
            m_AdditionalLightsShadowPass.Render(context, ref renderingData);
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
        // Is post processing enabled
        bool applyPostProcessingEffects = m_PostProcessingPass.isValid;
        // Only game camera and scene camera have post processing effects
        applyPostProcessingEffects &= camera.cameraType <= CameraType.SceneView;
        // Check if disable post processing effects in scene view
        applyPostProcessingEffects &= CoreUtils.ArePostProcessesEnabled(camera);

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
            m_PostProcessingPass.Setup(in m_ActiveCameraColorAttachment);
            m_PostProcessingPass.Execute(context, ref renderingData, ref m_ActiveCameraColorAttachment);
        }

        DrawGizmos(context, cmd, camera, GizmoSubset.PostImageEffects);

        // Finish rendering
        m_ColorBufferSystem.Clear();
        m_ActiveCameraColorAttachment = null;
        m_ActiveCameraDepthAttachment = null;
    }

    public void Dispose(bool disposing)
    {
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
            cmd.SetGlobalTexture("_CameraColorTexture", m_ActiveCameraColorAttachment.nameID);
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
            cmd.SetGlobalTexture(m_CameraDepthAttachment.name, m_CameraDepthAttachment.nameID);
        }

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
    }
}
