using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class DrawSkyboxPass
{
    private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler("DrawSkyboxPass");

    private class PassData
    {
        public RendererList rendererList;
    }

    public void Render(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cmd = renderingData.commandBuffer;
        using (new ProfilingScope(cmd, s_ProfilingSampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            var rendererList = context.CreateSkyboxRendererList(renderingData.camera);

            ExecutePass(CommandBufferHelpers.GetRasterCommandBuffer(cmd), rendererList);
        }
    }

    public void DrawRenderGraphSkybox(RenderGraph renderGraph, TextureHandle colorTarget, TextureHandle depthTarget, ref RenderingData renderingData)
    {
        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_ProfilingSampler.name, out var passData, s_ProfilingSampler))
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

    private static void ExecutePass(RasterCommandBuffer cmd, RendererList rendererList)
    {
        cmd.DrawRendererList(rendererList);
    }
}
