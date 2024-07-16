using UnityEngine.Rendering;

public class DrawObjectsForwardPass
{
    private static readonly ProfilingSampler m_DrawOpaqueObjectsSampler = new ProfilingSampler("DrawOpaqueObjectsPass");
    private static readonly ProfilingSampler m_DrawTransparentObjectsSampler = new ProfilingSampler("DrawTransparentObjectsPass");

    private bool m_IsOpaque;

    public DrawObjectsForwardPass(bool isOpaque)
    {
        m_IsOpaque = isOpaque;
    }

    public void Render(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cmd = renderingData.commandBuffer;
        var sampler = m_IsOpaque ? m_DrawOpaqueObjectsSampler : m_DrawTransparentObjectsSampler;
        using (new ProfilingScope(cmd, sampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            var sortFlags = m_IsOpaque ? SortingCriteria.CommonOpaque : SortingCriteria.CommonTransparent;
            var drawingSettings = RenderingUtils.CreateDrawingSettings(ref renderingData, sortFlags);
            var filteringSettings = m_IsOpaque ? new FilteringSettings(RenderQueueRange.opaque) : new FilteringSettings(RenderQueueRange.transparent);
            context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings);
        }
    }
}
