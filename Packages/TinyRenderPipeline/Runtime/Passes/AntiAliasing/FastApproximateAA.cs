using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class FastApproximateAA
{
    private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler("FXAA");

    private static int m_SourceSizeID = Shader.PropertyToID("_SourceSize");

    private Material m_FXAAMaterial;
    private RTHandle m_Source;

    private class PassData
    {
        public TextureHandle sourceTextureHdl;
        public TextureHandle targetTextureHdl;
        public Material material;
        public RenderingData renderingData;
    }

    public FastApproximateAA(Shader fxaaShader)
    {
        if (fxaaShader)
            m_FXAAMaterial = CoreUtils.CreateEngineMaterial(fxaaShader);
    }

    public void RecordRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle target, ref RenderingData renderingData)
    {
        if (m_FXAAMaterial == null)
        {
            Debug.LogError("FXAA Pass: FXAA material is null.");
            return;
        }

        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_ProfilingSampler.name, out var passData, s_ProfilingSampler))
        {
            passData.sourceTextureHdl = builder.UseTexture(source, IBaseRenderGraphBuilder.AccessFlags.Read);
            passData.material = m_FXAAMaterial;
            passData.renderingData = renderingData;

            passData.targetTextureHdl = builder.UseTextureFragment(target, 0, IBaseRenderGraphBuilder.AccessFlags.Write);

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext rasterGraphContext) =>
            {
                var cmd = rasterGraphContext.cmd;
                SetSourceSize(cmd, data.sourceTextureHdl);
                RenderingUtils.ScaleViewportAndBlit(cmd, data.sourceTextureHdl, data.targetTextureHdl, ref data.renderingData, data.material);
            });
        }
    }

    public void Dispose()
    {
        CoreUtils.Destroy(m_FXAAMaterial);
    }

    private static void SetSourceSize(RasterCommandBuffer cmd, RTHandle source)
         {
             float width = source.rt.width;
             float height = source.rt.height;
             cmd.SetGlobalVector(m_SourceSizeID, new Vector4(width, height, 1.0f / width, 1.0f / height));
         }
}
