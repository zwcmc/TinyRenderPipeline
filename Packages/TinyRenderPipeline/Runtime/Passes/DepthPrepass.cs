using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class DepthPrepass
{
    private static readonly ProfilingSampler s_DepthPrepassSampler = new ("Depth Prepass");

    private static readonly ShaderTagId k_ShaderTagId = new ("TinyRPDepth");

    private FilteringSettings m_FilteringSettings;

    private class PassData
    {
        public RendererListHandle rendererList;
    }

    public DepthPrepass()
    {
        m_FilteringSettings = new FilteringSettings(RenderQueueRange.opaque);
    }

    public void Record(RenderGraph renderGraph, ref TextureHandle cameraDepthTexture, ref RenderingData renderingData)
    {
        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_DepthPrepassSampler.name, out var passData, s_DepthPrepassSampler))
        {
            var sortingSettings = new SortingSettings(renderingData.camera) { criteria = SortingCriteria.CommonOpaque };
            var drawSettings = new DrawingSettings(k_ShaderTagId, sortingSettings)
            {
                perObjectData = PerObjectData.None,
                mainLightIndex = renderingData.mainLightIndex,

                enableDynamicBatching = false,
                enableInstancing = renderingData.camera.cameraType == CameraType.Preview ? false : true
            };

            var param = new RendererListParams(renderingData.cullResults, drawSettings, m_FilteringSettings);
            passData.rendererList = renderGraph.CreateRendererList(param);
            builder.UseRendererList(passData.rendererList);

            if (cameraDepthTexture.IsValid())
                builder.UseTextureFragmentDepth(cameraDepthTexture, IBaseRenderGraphBuilder.AccessFlags.Write);

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
            {
                context.cmd.DrawRendererList(data.rendererList);
            });
        }

        RenderingUtils.SetGlobalRenderGraphTextureName(renderGraph, "_CameraDepthTexture", cameraDepthTexture, "SetGlobalCameraDepthTexture");
    }
}
