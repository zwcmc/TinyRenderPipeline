using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class DrawSkyboxPass
{
    private static readonly ProfilingSampler m_ProfilingSampler = new ProfilingSampler("DrawSkyboxPass");

    private class PassData
    {
        public RendererList rendererList;
    }

    private static void ExecutePass(RasterCommandBuffer cmd, RendererList rendererList)
    {
        cmd.DrawRendererList(rendererList);
    }

    public void Render(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cmd = renderingData.commandBuffer;
        using (new ProfilingScope(cmd, m_ProfilingSampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            var rendererList = context.CreateSkyboxRendererList(renderingData.camera);

            ExecutePass(CommandBufferHelpers.GetRasterCommandBuffer(cmd), rendererList);
        }
    }

    public void DrawRenderGraphSkybox(RenderGraph renderGraph, TextureHandle colorTarget, TextureHandle depthTarget, ref RenderingData renderingData)
    {
        using (var builder = renderGraph.AddRasterRenderPass<PassData>(m_ProfilingSampler.name, out var passData, m_ProfilingSampler))
        {
            passData.rendererList = renderingData.renderContext.CreateSkyboxRendererList(renderingData.camera);

            builder.UseTextureFragment(colorTarget, 0, IBaseRenderGraphBuilder.AccessFlags.Write);
            builder.UseTextureFragmentDepth(depthTarget, IBaseRenderGraphBuilder.AccessFlags.Write);

            builder.AllowPassCulling(false);

            builder.SetRenderFunc((PassData data, RasterGraphContext rasterGraphContext) =>
            {
                ExecutePass(rasterGraphContext.cmd, data.rendererList);
            });
        }
    }
}
