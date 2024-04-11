using UnityEngine;
using UnityEngine.Rendering;

public class ColorGradingLutPass
{
    private readonly Material m_LutBuilder;
    private static readonly ProfilingSampler m_ProfilingSampler = new ProfilingSampler("ColorGradingLUT");

    public RTHandle m_ColorGradingLut;

    private static class ShaderConstants
    {
        public static readonly int _Lut_Params = Shader.PropertyToID("_Lut_Params");
    }

    public ColorGradingLutPass(PostProcessingData postProcessingData)
    {
        if (postProcessingData != null)
        {
            m_LutBuilder = CoreUtils.CreateEngineMaterial(postProcessingData.shaders.lutBuilderShader);
        }
    }

    public void ExecutePass(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cmd = renderingData.commandBuffer;

        // Set render target
        cmd.SetRenderTarget(m_ColorGradingLut, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);

        using (new ProfilingScope(cmd, m_ProfilingSampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            m_LutBuilder.shaderKeywords = null;

            int lutHeight = renderingData.lutSize;
            int lutWidth = lutHeight * lutHeight;
            var lutParameters = new Vector4(lutHeight, 0.5f / lutWidth, 0.5f / lutHeight, lutHeight / (lutHeight - 1f));

            m_LutBuilder.SetVector(ShaderConstants._Lut_Params, lutParameters);

            var asset = TinyRenderPipeline.asset;
            switch (asset.postProcessingData.tonemapping.mode)
            {
                case PostProcessingData.TonemappingMode.Neutral:
                    m_LutBuilder.EnableKeyword(ShaderKeywordStrings.TonemapNeutral);
                    break;
                case PostProcessingData.TonemappingMode.ACES:
                    m_LutBuilder.EnableKeyword(ShaderKeywordStrings.TonemapACES);
                    break;
                default:
                    break;
            }

            Blitter.BlitTexture(cmd, m_ColorGradingLut, Vector2.one, m_LutBuilder, 0);
        }
    }

    public void Dispose()
    {
        CoreUtils.Destroy(m_LutBuilder);

        m_ColorGradingLut?.Release();
    }
}
