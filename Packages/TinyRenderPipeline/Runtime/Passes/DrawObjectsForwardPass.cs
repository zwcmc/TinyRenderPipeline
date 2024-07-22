using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;

public class DrawObjectsForwardPass
{
    private static readonly ProfilingSampler s_DrawOpaqueObjectsSampler = new ProfilingSampler("DrawOpaqueObjectsPass");
    private static readonly ProfilingSampler s_DrawTransparentObjectsSampler = new ProfilingSampler("DrawTransparentObjectsPass");

    private bool m_IsOpaque;

    private class PassData
    {
        public RendererList rendererList;
        public RendererListHandle rendererListHandle;
    }

    private PassData m_PassData;

    public DrawObjectsForwardPass(bool isOpaque = false)
    {
        m_IsOpaque = isOpaque;
        m_PassData = new PassData();
    }

    public void Render(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cmd = renderingData.commandBuffer;
        var sampler = m_IsOpaque ? s_DrawOpaqueObjectsSampler : s_DrawTransparentObjectsSampler;
        using (new ProfilingScope(cmd, sampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            var sortFlags = m_IsOpaque ? SortingCriteria.CommonOpaque : SortingCriteria.CommonTransparent;
            var drawingSettings = RenderingUtils.CreateDrawingSettings(ref renderingData, sortFlags);
            var filteringSettings = m_IsOpaque ? new FilteringSettings(RenderQueueRange.opaque) : new FilteringSettings(RenderQueueRange.transparent);

            RenderingUtils.CreateRendererList(context, ref renderingData, drawingSettings, filteringSettings, ref m_PassData.rendererList);
            ExecutePass(CommandBufferHelpers.GetRasterCommandBuffer(cmd), m_PassData.rendererList);
        }
    }

    public void Record(RenderGraph renderGraph, TextureHandle colorTarget, TextureHandle depthTarget, TextureHandle mainShadowsTexture, TextureHandle additionalLightsShadowmap, ref RenderingData renderingData)
    {
        var sampler = m_IsOpaque ? s_DrawOpaqueObjectsSampler : s_DrawTransparentObjectsSampler;
        using (var builder = renderGraph.AddRasterRenderPass<PassData>(sampler.name, out var passData, sampler))
        {
            if (colorTarget.IsValid())
                builder.UseTextureFragment(colorTarget, 0, IBaseRenderGraphBuilder.AccessFlags.Write);
            if (depthTarget.IsValid())
                builder.UseTextureFragmentDepth(depthTarget, IBaseRenderGraphBuilder.AccessFlags.Write);

            if (mainShadowsTexture.IsValid())
                builder.UseTexture(mainShadowsTexture, IBaseRenderGraphBuilder.AccessFlags.Read);
            if (additionalLightsShadowmap.IsValid())
                builder.UseTexture(additionalLightsShadowmap, IBaseRenderGraphBuilder.AccessFlags.Read);

            var sortFlags = m_IsOpaque ? SortingCriteria.CommonOpaque : SortingCriteria.CommonTransparent;
            var filteringSettings = m_IsOpaque ? new FilteringSettings(RenderQueueRange.opaque) : new FilteringSettings(RenderQueueRange.transparent);
            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(ref renderingData, sortFlags);

            RenderingUtils.CreateRendererListWithRenderGraph(renderGraph, ref renderingData, drawingSettings, filteringSettings, ref passData.rendererListHandle);

            builder.UseRendererList(passData.rendererListHandle);

            builder.AllowPassCulling(false);

            builder.SetRenderFunc((PassData data, RasterGraphContext rasterGraphContext) =>
            {
                ExecutePass(rasterGraphContext.cmd, data.rendererListHandle);
            });
        }
    }

    private static void ExecutePass(RasterCommandBuffer cmd, RendererList rendererList)
    {
        cmd.DrawRendererList(rendererList);
    }
}
