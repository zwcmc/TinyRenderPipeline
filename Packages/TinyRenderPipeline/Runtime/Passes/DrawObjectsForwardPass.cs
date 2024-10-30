using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class DrawObjectsForwardPass
{
    private static readonly ProfilingSampler s_DrawOpaqueObjectsSampler = new ProfilingSampler("Opaque Objects");
    private static readonly ProfilingSampler s_DrawTransparentObjectsSampler = new ProfilingSampler("Transparent Objects");

    private bool m_IsOpaque;

    private class PassData
    {
        public RendererListHandle rendererListHandle;
        // Unsupported built-in shaders
        public RendererListHandle legacyRendererListHandle;
    }

    public DrawObjectsForwardPass(bool isOpaque = false)
    {
        m_IsOpaque = isOpaque;
    }

    public void Record(RenderGraph renderGraph, TextureHandle colorTarget, TextureHandle depthTarget, TextureHandle mainShadowsTexture, TextureHandle additionalLightsShadowMap, ref RenderingData renderingData)
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
            if (additionalLightsShadowMap.IsValid())
                builder.UseTexture(additionalLightsShadowMap, IBaseRenderGraphBuilder.AccessFlags.Read);

            var filteringSettings = m_IsOpaque ? new FilteringSettings(RenderQueueRange.opaque) : new FilteringSettings(RenderQueueRange.transparent);
            var sortingCriteria = m_IsOpaque ? SortingCriteria.CommonOpaque : SortingCriteria.CommonTransparent;

            RenderingUtils.CreateRendererListHandle(renderGraph, ref renderingData, filteringSettings, sortingCriteria, ref passData.rendererListHandle);
            RenderingUtils.CreateRendererListHandleWithLegacyShaderPassNames(renderGraph, ref renderingData, filteringSettings, sortingCriteria, ref passData.legacyRendererListHandle);

            builder.UseRendererList(passData.rendererListHandle);
            builder.UseRendererList(passData.legacyRendererListHandle);

            builder.AllowPassCulling(false);

            builder.SetRenderFunc((PassData data, RasterGraphContext rasterGraphContext) =>
            {
                ExecutePass(rasterGraphContext.cmd, data.rendererListHandle, data.legacyRendererListHandle);
            });
        }
    }

    private static void ExecutePass(RasterCommandBuffer cmd, RendererList rendererList, RendererList legacyRendererList)
    {
        cmd.DrawRendererList(rendererList);

        // Drawing unsupported legacy shader pass name objects with error in UNITY_EDITOR or DEVELOPMENT_BUILD modes
        RenderingUtils.DrawLegacyShaderPassNameObjectsWithError(cmd, ref legacyRendererList);
    }
}
