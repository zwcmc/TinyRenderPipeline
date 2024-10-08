using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class FXAAPass
{
    private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler("ApplyFXAA");

    private PostProcessingData m_PostProcessingData;
    private Material m_FXAAMaterial;
    private RTHandle m_Source;

    private class PassData
    {
        public TextureHandle sourceTextureHdl;
        public TextureHandle targetTextureHdl;
        public Material material;
        public RenderingData renderingData;
    }

    public void Render(ScriptableRenderContext context, in RTHandle source, PostProcessingData postProcessingData, ref RenderingData renderingData)
    {
        m_PostProcessingData = postProcessingData;
        if (m_PostProcessingData == null)
        {
            Debug.LogError("FXAA Pass: post-processing data is null.");
            return;
        }

        if (m_FXAAMaterial == null && m_PostProcessingData != null)
        {
            m_FXAAMaterial = CoreUtils.CreateEngineMaterial(m_PostProcessingData.shaders.fxaaShader);
        }

        if (m_FXAAMaterial == null)
        {
            Debug.LogError("FXAA Pass: material is null");
            return;
        }

        m_Source = source;

        var cmd = renderingData.commandBuffer;
        using (new ProfilingScope(cmd, s_ProfilingSampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            SetSourceSize(CommandBufferHelpers.GetRasterCommandBuffer(cmd), m_Source);

            RenderingUtils.FinalBlit(cmd, renderingData.camera, m_Source, TinyRenderPipeline.k_CameraTarget, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                m_FXAAMaterial, 0);
        }
    }

    public void Record(RenderGraph renderGraph, TextureHandle source, TextureHandle target, PostProcessingData postProcessingData, ref RenderingData renderingData)
    {
        m_PostProcessingData = postProcessingData;
        if (m_PostProcessingData == null)
        {
            Debug.LogError("FXAA Pass: post-processing data is null.");
            return;
        }

        m_FXAAMaterial = CoreUtils.CreateEngineMaterial(m_PostProcessingData.shaders.fxaaShader);

        if (m_FXAAMaterial == null)
        {
            Debug.LogError("FXAA Pass: material is null");
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
        cmd.SetGlobalVector(ShaderPropertyId.sourceSize, new Vector4(width, height, 1.0f / width, 1.0f / height));
    }
}
