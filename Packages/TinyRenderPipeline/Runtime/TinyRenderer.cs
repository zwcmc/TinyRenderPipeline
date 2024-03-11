#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityEngine;
using UnityEngine.Rendering;

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

    public TinyRenderer()
    {
        m_ForwardLights = new ForwardLights();
        m_MainLightShadowPass = new MainLightShadowPass();
        m_AdditionalLightsShadowPass = new AdditionalLightsShadowPass();
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
        if (m_AdditionalLightsShadowPass.Setup())
        {
            m_AdditionalLightsShadowPass.Render();
        }

        // Setup camera properties
        SetCameraProperties(context, camera);

        // Configure target
        cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
        CameraClearFlags flags = camera.clearFlags;
        cmd.ClearRenderTarget(flags <= CameraClearFlags.Depth, flags <= CameraClearFlags.Color,
            flags == CameraClearFlags.Color ? camera.backgroundColor.linear : Color.clear);
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        DrawOpaque(context, ref renderingData);

        DrawSkybox(context, cmd, camera);

        DrawTransparent(context, ref renderingData);

        DrawGizmos(context, cmd, camera);
    }

    public void Dispose(bool disposing)
    {
        m_MainLightShadowPass?.Dispose();
        m_AdditionalLightsShadowPass?.Dispose();
    }

    private void SetCameraProperties(ScriptableRenderContext context, Camera camera)
    {
        context.SetupCameraProperties(camera);
    }

    private void ClearRenderTarget(CommandBuffer cmd, Camera camera)
    {
        CameraClearFlags flags = camera.clearFlags;
        cmd.ClearRenderTarget(
            flags <= CameraClearFlags.Depth,
            flags <= CameraClearFlags.Color,
            flags == CameraClearFlags.Color ? camera.backgroundColor.linear : Color.clear);
    }

    private void DrawGizmos(ScriptableRenderContext context, CommandBuffer cmd, Camera camera)
    {
#if UNITY_EDITOR
        if (!Handles.ShouldRenderGizmos())
            return;

        using (new ProfilingScope(cmd, Profiling.drawGizmos))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            context.DrawGizmos(camera, GizmoSubset.PreImageEffects);
            context.DrawGizmos(camera, GizmoSubset.PostImageEffects);
        }

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
#endif
    }
}
