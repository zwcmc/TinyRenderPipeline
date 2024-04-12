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

        // Color adjustments
        public static readonly int _HueSatConPos = Shader.PropertyToID("_HueSatConPos");
        public static readonly int _ColorFilter = Shader.PropertyToID("_ColorFilter");

        // White balance
        public static readonly int _ColorBalance = Shader.PropertyToID("_ColorBalance");
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

            var colorAdjustments = asset.postProcessingData.colorAdjustments;
            var whiteBalance = asset.postProcessingData.whiteBalance;

            float postExposureLinear = Mathf.Pow(2f, colorAdjustments.postExposure);
            var hueSatConPos = new Vector4(colorAdjustments.hueShift / 360f, colorAdjustments.saturation * 0.01f + 1f, colorAdjustments.contrast * 0.01f + 1f, postExposureLinear);
            var lmsColorBalance = ColorUtils.ColorBalanceToLMSCoeffs(whiteBalance.temperature, whiteBalance.tint);

            m_LutBuilder.SetVector(ShaderConstants._HueSatConPos, hueSatConPos);
            m_LutBuilder.SetVector(ShaderConstants._ColorFilter, colorAdjustments.colorFilter);
            m_LutBuilder.SetVector(ShaderConstants._ColorBalance, lmsColorBalance);

            var tonemapping = asset.postProcessingData.tonemapping;
            switch (tonemapping.mode)
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
