using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class ColorGradingLut
{
    private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler("ColorGrading LUT");

    private Material m_LutBuilderMaterial;
    private PostProcessingData m_PostProcessingData;

    private static class ShaderConstants
    {
        public static readonly int _Lut_Params = Shader.PropertyToID("_Lut_Params");

        // Color adjustments
        public static readonly int _HueSatConPos = Shader.PropertyToID("_HueSatConPos");
        public static readonly int _ColorFilter = Shader.PropertyToID("_ColorFilter");

        // White balance
        public static readonly int _ColorBalance = Shader.PropertyToID("_ColorBalance");
    }

    private class PassData
    {
        public TextureHandle lutTextureHdl;
        public Material lutBuilderMaterial;
        public RenderingData renderingData;
        public PostProcessingData postProcessingData;
    }

    public void RecordRenderGraph(RenderGraph renderGraph, out TextureHandle lutTarget, ref RenderingData renderingData)
    {
        m_PostProcessingData = renderingData.postProcessingData;
        if (m_PostProcessingData == null)
        {
            Debug.LogError("Color Grading Lut Pass: post-processing data is null.");
            lutTarget = TextureHandle.nullHandle;
            return;
        }

        if (m_LutBuilderMaterial == null && m_PostProcessingData != null)
        {
            m_LutBuilderMaterial = CoreUtils.CreateEngineMaterial(m_PostProcessingData.shaders.lutBuilderShader);
        }

        if (m_LutBuilderMaterial == null)
        {
            Debug.LogError("Color Grading Lut Pass: lut builder material is null.");
            lutTarget = TextureHandle.nullHandle;
            return;
        }

        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_ProfilingSampler.name, out var passData, s_ProfilingSampler))
        {
            int lutHeight = renderingData.lutSize;
            int lutWidth = lutHeight * lutHeight;
            var lutFormat = renderingData.defaultFormat;
            var descriptor = new RenderTextureDescriptor(lutWidth, lutHeight, lutFormat, 0);
            lutTarget = RenderingUtils.CreateRenderGraphTexture(renderGraph, descriptor, "_InternalGradingLut", false, FilterMode.Bilinear);

            passData.lutTextureHdl = builder.UseTextureFragment(lutTarget, 0, IBaseRenderGraphBuilder.AccessFlags.WriteAll);

            passData.lutBuilderMaterial = m_LutBuilderMaterial;
            passData.renderingData = renderingData;
            passData.postProcessingData = m_PostProcessingData;

            builder.AllowPassCulling(false);

            builder.SetRenderFunc((PassData data, RasterGraphContext rasterGraphContext) =>
            {
                ExecutePass(rasterGraphContext.cmd, data.lutTextureHdl, data);
            });
        }
    }

    public void Dispose()
    {
        CoreUtils.Destroy(m_LutBuilderMaterial);
    }

    private static void ExecutePass(RasterCommandBuffer cmd, RTHandle lutTarget, PassData data)
    {
        var material = data.lutBuilderMaterial;

        material.shaderKeywords = null;

        ref var renderingData = ref data.renderingData;
        int lutHeight = renderingData.lutSize;
        int lutWidth = lutHeight * lutHeight;

        var lutParams = new Vector4(lutHeight, 0.5f / lutWidth, 0.5f / lutHeight, lutHeight / (lutHeight - 1f));
        material.SetVector(ShaderConstants._Lut_Params, lutParams);

        var postProcessingData = data.postProcessingData;
        var colorAdjustments = postProcessingData.colorAdjustments;
        var whiteBalance = postProcessingData.whiteBalance;

        float postExposureLinear = Mathf.Pow(2f, colorAdjustments.postExposure);
        var hueSatConPos = new Vector4(colorAdjustments.hueShift / 360f, colorAdjustments.saturation * 0.01f + 1f, colorAdjustments.contrast * 0.01f + 1f, postExposureLinear);
        var lmsColorBalance = ColorUtils.ColorBalanceToLMSCoeffs(whiteBalance.temperature, whiteBalance.tint);

        material.SetVector(ShaderConstants._HueSatConPos, hueSatConPos);
        material.SetVector(ShaderConstants._ColorFilter, colorAdjustments.colorFilter);
        material.SetVector(ShaderConstants._ColorBalance, lmsColorBalance);

        var tonemapping = postProcessingData.tonemapping;
        switch (tonemapping.mode)
        {
            case PostProcessingData.TonemappingMode.Neutral:
                material.EnableKeyword(ShaderKeywordStrings.TonemapNeutral);
                break;
            case PostProcessingData.TonemappingMode.ACES:
                material.EnableKeyword(ShaderKeywordStrings.TonemapACES);
                break;
        }

        Blitter.BlitTexture(cmd, lutTarget, new Vector4(1f, 1f, 0f, 0f), material, 0);
    }
}
