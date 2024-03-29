using UnityEngine.Rendering;

public partial class TinyRenderer
{
    void DrawOpaque(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cmd = renderingData.commandBuffer;
        using (new ProfilingScope(cmd, Profiling.drawOpaque))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            var sortFlags = SortingCriteria.CommonOpaque;
            var cullResults = renderingData.cullResults;
            var drawingSettings = RenderingUtils.CreateDrawingSettings(ref renderingData, sortFlags);
            var filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
            context.DrawRenderers(cullResults, ref drawingSettings, ref filteringSettings);
        }

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
    }

    void DrawTransparent(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cmd = renderingData.commandBuffer;
        using (new ProfilingScope(cmd, Profiling.drawTransparent))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            var sortFlags = SortingCriteria.CommonTransparent;
            var cullResults = renderingData.cullResults;
            var drawingSettings = RenderingUtils.CreateDrawingSettings(ref renderingData, sortFlags);
            var filteringSettings = new FilteringSettings(RenderQueueRange.transparent);
            context.DrawRenderers(cullResults, ref drawingSettings, ref filteringSettings);
        }

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
    }
}
