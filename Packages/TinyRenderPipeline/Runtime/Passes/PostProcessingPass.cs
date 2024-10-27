using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class PostProcessingPass
{
    private static class Profiling
    {
        public static readonly ProfilingSampler s_UberPass = new ("UberPass");

        // Render Graph Samplers
        public static readonly ProfilingSampler s_BloomMaterialSetup = new ProfilingSampler("BloomMaterialSetup");
        public static readonly ProfilingSampler s_BloomPrefilter = new ProfilingSampler("BloomPrefilter");
        public static readonly ProfilingSampler s_BloomBlurHorizontal = new ProfilingSampler("BloomBlurHorizontal");
        public static readonly ProfilingSampler s_BloomBlurVertical = new ProfilingSampler("BloomBlurVertical");
        public static readonly ProfilingSampler s_BloomUpsample = new ProfilingSampler("BloomUpsample");
        public static readonly ProfilingSampler s_UberPassSetupBloom = new ProfilingSampler("UberPassSetupBloom");
    }

    private Material m_PostProcessingMaterial;

    private RenderTextureDescriptor m_Descriptor;
    private RTHandle m_Source;

    // private bool m_ResolveToScreen;

    private RTHandle m_InternalLut;

    private MaterialLibrary m_Materials;

    // Post processing effects settings
    private PostProcessingData.Bloom m_Bloom;

    // Bloom
    private const int k_MaxPyramidSize = 16;
    private GraphicsFormat m_DefaultHDRFormat;
    private RTHandle[] m_BloomMipDown;
    private RTHandle[] m_BloomMipUp;

    private TextureHandle[] m_RenderGraphBloomMipDown;
    private TextureHandle[] m_RenderGraphBloomMipUp;

    private PostProcessingData m_PostProcessingData;

    private static class ShaderConstants
    {
        public static readonly int _SourceTexLowMip = Shader.PropertyToID("_SourceTexLowMip");

        public static readonly int _BloomParams = Shader.PropertyToID("_BloomParams");

        public static readonly int _BloomIntensity = Shader.PropertyToID("_BloomIntensity");

        public static readonly int _Bloom_Texture = Shader.PropertyToID("_Bloom_Texture");

        public static readonly int _InternalLut = Shader.PropertyToID("_InternalLut");
        public static readonly int _Lut_Params = Shader.PropertyToID("_Lut_Params");

        public static int[] _BloomMipUp;
        public static int[] _BloomMipDown;
    }

    private class BloomMaterialSetupPassData
    {
        public Vector4 bloomParams;
        public bool highQualityFiltering;
        public Material bloomMaterial;
    }

    private class BloomPassData
    {
        public TextureHandle sourceTextureHdl;
        public TextureHandle sourceTextureLowMipHdl;
        public Material material;
        public float bloomIntensity;
    }

    private class UberPassData
    {
        public TextureHandle sourceTextureHdl;
        public TextureHandle targetTextureHdl;
        public RenderingData renderingData;
        public Material material;
        public TextureHandle lutTextureHdl;
        public Vector4 lutParams;
    }

    public PostProcessingPass()
    {
        ShaderConstants._BloomMipUp = new int[k_MaxPyramidSize];
        ShaderConstants._BloomMipDown = new int[k_MaxPyramidSize];
        m_BloomMipUp = new RTHandle[k_MaxPyramidSize];
        m_BloomMipDown = new RTHandle[k_MaxPyramidSize];

        // Render Graph Bloom TextureHandles
        m_RenderGraphBloomMipUp = new TextureHandle[k_MaxPyramidSize];
        m_RenderGraphBloomMipDown = new TextureHandle[k_MaxPyramidSize];

        for (int i = 0; i < k_MaxPyramidSize; ++i)
        {
            ShaderConstants._BloomMipUp[i] = Shader.PropertyToID("_BloomMipUp" + i);
            ShaderConstants._BloomMipDown[i] = Shader.PropertyToID("_BloomMipDown" + i);

            m_BloomMipUp[i] = RTHandles.Alloc(ShaderConstants._BloomMipUp[i], name: "_BloomMipUp" + i);
            m_BloomMipDown[i] = RTHandles.Alloc(ShaderConstants._BloomMipDown[i], name: "_BloomMipDown" + i);
        }
    }

    public void Record(RenderGraph renderGraph, in TextureHandle source, TextureHandle colorLut, TextureHandle target, PostProcessingData postProcessingData, ref RenderingData renderingData)
    {
        m_PostProcessingData = postProcessingData;

        if (m_PostProcessingData == null)
        {
            Debug.LogError("Post Processing Pass: post-processing data is null.");
            return;
        }

        m_Materials = new MaterialLibrary(m_PostProcessingData);
        if (m_Materials == null)
        {
            Debug.LogError("Post Processing Pass: post-processing materials is null.");
            return;
        }

        m_Descriptor = renderingData.cameraTargetDescriptor;
        // m_ResolveToScreen = resolveToScreen;

        m_Bloom = m_PostProcessingData.bloom;
        m_DefaultHDRFormat = renderingData.defaultFormat;

        // Reset uber pass keywords
        m_Materials.uberPost.shaderKeywords = null;

        // Bloom
        if (m_Bloom.IsActive())
        {
            // Render bloom texture
            RenderBloomTexture(renderGraph, source, out var bloomTexture);
            // Setup bloom on uber pass
            SetupRenderGraphUberPassBloom(renderGraph, bloomTexture, m_Materials.uberPost);
        }

        // Setup color grading on uber pass
        SetupRenderGraphColorGrading(renderGraph, colorLut, ref renderingData, m_Materials.uberPost);

        // Final uber pass
        RenderGraphRenderUberPass(renderGraph, source, target, m_Materials.uberPost, ref renderingData);
    }

    public void Dispose()
    {
        foreach (var handle in m_BloomMipUp)
            handle?.Release();
        foreach (var handle in m_BloomMipDown)
            handle?.Release();

        m_Materials?.Cleanup();
    }

    private void RenderBloomTexture(RenderGraph renderGraph, in TextureHandle source, out TextureHandle destination)
    {
        // Start at half size
        int tw = m_Descriptor.width >> 1;
        int th = m_Descriptor.height >> 1;

        // Determine the iteration count
        int maxSize = Mathf.Max(tw, th);
        int iterations = Mathf.FloorToInt(Mathf.Log(maxSize, 2f) - 1);
        int mipCount = Mathf.Clamp(iterations, 1, m_Bloom.maxIterations);

        var bloomMaterial = m_Materials.bloom;

        // Bloom material setup
        using (var builder = renderGraph.AddRasterRenderPass<BloomMaterialSetupPassData>(Profiling.s_BloomMaterialSetup.name, out var passData, Profiling.s_BloomMaterialSetup))
        {
            // Pre-filtering parameters
            float clamp = m_Bloom.clamp;
            float threshold = Mathf.GammaToLinearSpace(m_Bloom.threshold);
            float thresholdKnee = threshold * 0.5f;

            float scatter = Mathf.Lerp(0.05f, 0.95f, m_Bloom.scatter);

            passData.bloomParams = new Vector4(scatter, clamp, threshold, thresholdKnee);
            passData.bloomMaterial = bloomMaterial;
            passData.highQualityFiltering = m_Bloom.highQualityFiltering;

            builder.AllowPassCulling(false);

            builder.SetRenderFunc((BloomMaterialSetupPassData data, RasterGraphContext rasterGraphContext) =>
            {
                var material = data.bloomMaterial;
                material.SetVector(ShaderConstants._BloomParams, data.bloomParams);
                CoreUtils.SetKeyword(material, ShaderKeywordStrings.BloomHQ, data.highQualityFiltering);
            });
        }

        // Prefilter
        var desc = RenderingUtils.GetCompatibleDescriptor(m_Descriptor, tw, th, m_DefaultHDRFormat);
        m_RenderGraphBloomMipDown[0] = RenderingUtils.CreateRenderGraphTexture(renderGraph, desc, "_BloomMipDown0", true, FilterMode.Bilinear);
        m_RenderGraphBloomMipUp[0] = RenderingUtils.CreateRenderGraphTexture(renderGraph, desc, "_BloomMipUp0", true, FilterMode.Bilinear);
        using (var builder = renderGraph.AddRasterRenderPass<BloomPassData>(Profiling.s_BloomPrefilter.name, out var passData, Profiling.s_BloomPrefilter))
        {
            builder.UseTextureFragment(m_RenderGraphBloomMipDown[0], 0, IBaseRenderGraphBuilder.AccessFlags.Write);

            passData.sourceTextureHdl = builder.UseTexture(source, IBaseRenderGraphBuilder.AccessFlags.Read);
            passData.material = bloomMaterial;

            builder.SetRenderFunc((BloomPassData data, RasterGraphContext rasterGraphContext) =>
            {
                Blitter.BlitTexture(rasterGraphContext.cmd, data.sourceTextureHdl, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
            });
        }

        // Downsample - gaussian pyramid
        TextureHandle lastDown = m_RenderGraphBloomMipDown[0];
        for (int i = 1; i < mipCount; i++)
        {
            tw = Mathf.Max(1, tw >> 1);
            th = Mathf.Max(1, th >> 1);

            ref TextureHandle mipDown = ref m_RenderGraphBloomMipDown[i];
            ref TextureHandle mipUp = ref m_RenderGraphBloomMipUp[i];

            desc.width = tw;
            desc.height = th;

            mipDown = RenderingUtils.CreateRenderGraphTexture(renderGraph, desc, "_BloomMipDown" + i, true, FilterMode.Bilinear);
            mipUp = RenderingUtils.CreateRenderGraphTexture(renderGraph, desc, "_BloomMipUp" + i, true, FilterMode.Bilinear);

            // Classic two pass gaussian blur - use mipUp as a temporary target
            //   First pass does 2x downsampling + 9-tap gaussian
            //   Second pass does 9-tap gaussian using a 5-tap filter + bilinear filtering
            using (var builder = renderGraph.AddRasterRenderPass<BloomPassData>(Profiling.s_BloomBlurHorizontal.name, out var passData, Profiling.s_BloomBlurHorizontal))
            {
                builder.UseTextureFragment(mipUp, 0, IBaseRenderGraphBuilder.AccessFlags.Write);

                passData.sourceTextureHdl = builder.UseTexture(lastDown, IBaseRenderGraphBuilder.AccessFlags.Read);
                passData.material = bloomMaterial;

                builder.SetRenderFunc((BloomPassData data, RasterGraphContext rasterGraphContext) =>
                {
                    Blitter.BlitTexture(rasterGraphContext.cmd, data.sourceTextureHdl, new Vector4(1f, 1f, 0f, 0f), data.material, 1);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<BloomPassData>(Profiling.s_BloomBlurVertical.name, out var passData, Profiling.s_BloomBlurVertical))
            {
                builder.UseTextureFragment(mipDown, 0, IBaseRenderGraphBuilder.AccessFlags.Write);

                passData.sourceTextureHdl = builder.UseTexture(mipUp, IBaseRenderGraphBuilder.AccessFlags.Read);
                passData.material = bloomMaterial;

                builder.SetRenderFunc((BloomPassData data, RasterGraphContext rasterGraphContext) =>
                {
                    Blitter.BlitTexture(rasterGraphContext.cmd, data.sourceTextureHdl, new Vector4(1f, 1f, 0f, 0f), data.material, 2);
                });
            }

            lastDown = mipDown;
        }

        // Upsample (bilinear by default, HQ filtering does bicubic instead
        for (int i = mipCount - 2; i >= 0; i--)
        {
            TextureHandle lowMip = (i == mipCount - 2) ? m_RenderGraphBloomMipDown[i + 1] : m_RenderGraphBloomMipUp[i + 1];
            TextureHandle highMip = m_RenderGraphBloomMipDown[i];
            TextureHandle dst = m_RenderGraphBloomMipUp[i];

            using (var builder = renderGraph.AddRasterRenderPass<BloomPassData>(Profiling.s_BloomUpsample.name, out var passData, Profiling.s_BloomUpsample))
            {
                builder.UseTextureFragment(dst, 0, IBaseRenderGraphBuilder.AccessFlags.Write);

                passData.sourceTextureHdl = builder.UseTexture(highMip, IBaseRenderGraphBuilder.AccessFlags.Read);
                passData.sourceTextureLowMipHdl = builder.UseTexture(lowMip, IBaseRenderGraphBuilder.AccessFlags.Read);
                passData.material = bloomMaterial;

                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((BloomPassData data, RasterGraphContext rasterGraphContext) =>
                {
                    var cmd = rasterGraphContext.cmd;
                    cmd.SetGlobalTexture(ShaderConstants._SourceTexLowMip, data.sourceTextureLowMipHdl);
                    Blitter.BlitTexture(cmd, data.sourceTextureHdl, new Vector4(1f, 1f, 0f, 0f), data.material, 3);
                });
            }
        }

        destination = m_RenderGraphBloomMipUp[0];
    }

    private void SetupRenderGraphUberPassBloom(RenderGraph renderGraph, in TextureHandle bloomTexture, Material uberMaterial)
    {
        // Set global bloom texture and enable bloom in uber pass
        using (var builder = renderGraph.AddRasterRenderPass<BloomPassData>(Profiling.s_UberPassSetupBloom.name, out var passData, Profiling.s_UberPassSetupBloom))
        {
            passData.sourceTextureHdl = builder.UseTexture(bloomTexture, IBaseRenderGraphBuilder.AccessFlags.Read);
            passData.material = uberMaterial;
            passData.bloomIntensity = m_Bloom.intensity;

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((BloomPassData data, RasterGraphContext rasterGraphContext) =>
            {
                var material = data.material;
                material.EnableKeyword(ShaderKeywordStrings.Bloom);
                material.SetFloat(ShaderConstants._BloomIntensity, data.bloomIntensity);
                rasterGraphContext.cmd.SetGlobalTexture(ShaderConstants._Bloom_Texture, data.sourceTextureHdl);
            });
        }
    }

    private void SetupRenderGraphColorGrading(RenderGraph renderGraph, TextureHandle lutTexture, ref RenderingData renderingData, Material uberMaterial)
    {
        using (var builder = renderGraph.AddRasterRenderPass<UberPassData>("Setup Color Grading Lut Texture", out var passData))
        {
            if (lutTexture.IsValid())
                passData.lutTextureHdl = builder.UseTexture(lutTexture, IBaseRenderGraphBuilder.AccessFlags.Read);

            int lutHeight = renderingData.lutSize;
            int lutWidth = lutHeight * lutHeight;
            passData.lutParams = new Vector4(1f / lutWidth, 1f / lutHeight, lutHeight - 1f, 1f);
            passData.material = uberMaterial;
            passData.renderingData = renderingData;

            builder.AllowPassCulling(false);

            builder.SetRenderFunc((UberPassData data, RasterGraphContext rasterGraphContext) =>
            {
                var material = data.material;

                material.SetTexture(ShaderConstants._InternalLut, data.lutTextureHdl);
                material.SetVector(ShaderConstants._Lut_Params, data.lutParams);
                if (data.renderingData.isHdrEnabled)
                    material.EnableKeyword(ShaderKeywordStrings.HDRColorGrading);
            });
        }
    }

    private void RenderGraphRenderUberPass(RenderGraph renderGraph, TextureHandle source, TextureHandle target, Material uberMaterial, ref RenderingData renderingData)
    {
        using (var builder = renderGraph.AddRasterRenderPass<UberPassData>(Profiling.s_UberPass.name, out var passData, Profiling.s_UberPass))
        {
            if (m_Bloom.IsActive())
                builder.UseTexture(m_RenderGraphBloomMipUp[0], IBaseRenderGraphBuilder.AccessFlags.Read);

            passData.sourceTextureHdl = builder.UseTexture(source, IBaseRenderGraphBuilder.AccessFlags.Read);
            passData.targetTextureHdl = builder.UseTextureFragment(target, 0, IBaseRenderGraphBuilder.AccessFlags.Write);
            passData.renderingData = renderingData;
            passData.material = uberMaterial;

            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((UberPassData data, RasterGraphContext rasterGraphContext) =>
            {
                RenderingUtils.ScaleViewportAndBlit(rasterGraphContext.cmd, data.sourceTextureHdl, data.targetTextureHdl, ref data.renderingData, data.material);
            });
        }
    }

    private class MaterialLibrary
    {
        public readonly Material uberPost;
        public readonly Material bloom;

        public MaterialLibrary(PostProcessingData data)
        {
            uberPost = CoreUtils.CreateEngineMaterial(data.shaders.uberPostShader);
            bloom = CoreUtils.CreateEngineMaterial(data.shaders.bloomShader);
        }

        public void Cleanup()
        {
            CoreUtils.Destroy(uberPost);
            CoreUtils.Destroy(bloom);
        }
    }
}
