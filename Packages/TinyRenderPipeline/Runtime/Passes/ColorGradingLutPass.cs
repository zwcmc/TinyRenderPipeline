using UnityEngine;
using UnityEngine.Rendering;

public class ColorGradingLutPass
{
    private Material m_LutBuilder;
    private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler("ColorGradingLUT");

    private PostProcessingData m_PostProcessingData;

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

    public void Setup(PostProcessingData postProcessingData)
    {
        m_PostProcessingData = postProcessingData;

        if (m_LutBuilder == null && m_PostProcessingData != null)
        {
            m_LutBuilder = CoreUtils.CreateEngineMaterial(m_PostProcessingData.shaders.lutBuilderShader);
        }
    }

    public void Render(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (m_LutBuilder == null)
        {
            Debug.LogError("Color Grading Lut Pass: lut builder material is null.");
            return;
        }

        if (m_PostProcessingData == null)
        {
            Debug.LogError("Color Grading Lut Pass: post-processing data is null.");
            return;
        }

        var cmd = renderingData.commandBuffer;

        // Set render target
        cmd.SetRenderTarget(m_ColorGradingLut, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);

        using (new ProfilingScope(cmd, s_ProfilingSampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            m_LutBuilder.shaderKeywords = null;

            int lutHeight = renderingData.lutSize;
            int lutWidth = lutHeight * lutHeight;
            var lutParameters = new Vector4(lutHeight, 0.5f / lutWidth, 0.5f / lutHeight, lutHeight / (lutHeight - 1f));

            m_LutBuilder.SetVector(ShaderConstants._Lut_Params, lutParameters);

            var colorAdjustments = m_PostProcessingData.colorAdjustments;
            var whiteBalance = m_PostProcessingData.whiteBalance;

            float postExposureLinear = Mathf.Pow(2f, colorAdjustments.postExposure);
            var hueSatConPos = new Vector4(colorAdjustments.hueShift / 360f, colorAdjustments.saturation * 0.01f + 1f, colorAdjustments.contrast * 0.01f + 1f, postExposureLinear);
            var lmsColorBalance = ColorUtils.ColorBalanceToLMSCoeffs(whiteBalance.temperature, whiteBalance.tint);

            m_LutBuilder.SetVector(ShaderConstants._HueSatConPos, hueSatConPos);
            m_LutBuilder.SetVector(ShaderConstants._ColorFilter, colorAdjustments.colorFilter);
            m_LutBuilder.SetVector(ShaderConstants._ColorBalance, lmsColorBalance);

            var tonemapping = m_PostProcessingData.tonemapping;
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
