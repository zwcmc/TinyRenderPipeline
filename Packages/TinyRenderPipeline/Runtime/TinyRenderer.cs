#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityEngine;
using UnityEngine.Rendering;

public partial class TinyRenderer
{
    private static class Profiling
    {
        public static readonly ProfilingSampler drawGizmos = new ProfilingSampler($"{nameof(DrawGizmos)}");
    }

    public TinyRenderer() {}

    public void Execute(ref RenderingData renderingData)
    {
        var context = renderingData.renderContext;
        var camera = renderingData.camera;
        var cmd = renderingData.commandBuffer;

        SetCameraProperties(context, camera);

        ClearRenderTarget(cmd, camera);

        DrawSkybox(context, cmd, camera);

        DrawGizmos(context, cmd, camera);
    }

    public void Dispose(bool disposing)
    {

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
