using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class PostProcessingPass
{
    private static class Profiling
    {
        public static readonly ProfilingSampler s_ApplyPostProcessing = new ProfilingSampler("Apply Post Processing");

        public static readonly ProfilingSampler s_Bloom = new ProfilingSampler("Bloom");
        public static readonly ProfilingSampler s_BloomPrefilter = new ProfilingSampler("Prefilter");
        public static readonly ProfilingSampler s_BloomDownsample = new ProfilingSampler("Downsample");
        public static readonly ProfilingSampler s_BloomUpsample = new ProfilingSampler("Upsample");
        public static readonly ProfilingSampler s_UberPassSetupBloom = new ProfilingSampler("Uber Pass Setup Bloom");
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

    private class BloomPassData
    {
        public int mipCount;

        public Material material;
        public Material[] upsampleMaterials;

        public TextureHandle sourceTexture;

        public TextureHandle[] bloomMipUp;
        public TextureHandle[] bloomMipDown;

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

    public void RecordRenderGraph(RenderGraph renderGraph, in TextureHandle source, TextureHandle colorLut, TextureHandle target, ref RenderingData renderingData)
    {
        m_PostProcessingData = renderingData.postProcessingData;

        if (m_PostProcessingData == null)
        {
            Debug.LogError("Post Processing Pass: post-processing data is null.");
            return;
        }

        if (m_Materials == null)
        {
            m_Materials = new MaterialLibrary(m_PostProcessingData);
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

        // Pre-filtering parameters
        var bloomMaterial = m_Materials.bloom;
        float clamp = m_Bloom.clamp;
        float threshold = Mathf.GammaToLinearSpace(m_Bloom.threshold);
        float thresholdKnee = threshold * 0.5f;
        float scatter = Mathf.Lerp(0.05f, 0.95f, m_Bloom.scatter);
        Vector4 parameters = new Vector4(scatter, clamp, threshold, thresholdKnee);
        bloomMaterial.SetVector(ShaderConstants._BloomParams, parameters);
        CoreUtils.SetKeyword(bloomMaterial, ShaderKeywordStrings.BloomHQ, m_Bloom.highQualityFiltering);

        // These materials are duplicate just to allow different bloom blits to use different textures.
        for (uint i = 0; i < k_MaxPyramidSize; ++i)
        {
            var materialPyramid = m_Materials.bloomUpsample[i];
            materialPyramid.SetVector(ShaderConstants._BloomParams, parameters);
            CoreUtils.SetKeyword(materialPyramid, ShaderKeywordStrings.BloomHQ, m_Bloom.highQualityFiltering);
        }

        // Create bloom mip pyramid textures
        var desc = RenderingUtils.GetCompatibleDescriptor(m_Descriptor, tw, th, m_DefaultHDRFormat);
        m_RenderGraphBloomMipDown[0] = RenderingUtils.CreateRenderGraphTexture(renderGraph, desc, m_BloomMipDown[0].name, false, FilterMode.Bilinear);
        m_RenderGraphBloomMipUp[0] = RenderingUtils.CreateRenderGraphTexture(renderGraph, desc, m_BloomMipUp[0].name, false, FilterMode.Bilinear);

        for (int i = 1; i < mipCount; ++i)
        {
            tw = Mathf.Max(1, tw >> 1);
            th = Mathf.Max(1, th >> 1);

            ref TextureHandle mipDown = ref m_RenderGraphBloomMipDown[i];
            ref TextureHandle mipUp = ref m_RenderGraphBloomMipUp[i];

            desc.width = tw;
            desc.height = th;

            mipDown = RenderingUtils.CreateRenderGraphTexture(renderGraph, desc, m_BloomMipDown[i].name, false, FilterMode.Bilinear);
            mipUp = RenderingUtils.CreateRenderGraphTexture(renderGraph, desc, m_BloomMipUp[i].name, false, FilterMode.Bilinear);
        }

        using (var builder = renderGraph.AddLowLevelPass<BloomPassData>("Blit Bloom Mipmaps Chain", out var passData, Profiling.s_Bloom))
        {
            passData.mipCount = mipCount;
            passData.material = bloomMaterial;
            passData.upsampleMaterials = m_Materials.bloomUpsample;
            passData.sourceTexture = source;
            passData.bloomMipDown = m_RenderGraphBloomMipDown;
            passData.bloomMipUp = m_RenderGraphBloomMipUp;

            builder.AllowPassCulling(false);

            builder.UseTexture(source, IBaseRenderGraphBuilder.AccessFlags.Read);
            for (int i = 0; i < mipCount; i++)
            {
                builder.UseTexture(m_RenderGraphBloomMipDown[i], IBaseRenderGraphBuilder.AccessFlags.ReadWrite);
                builder.UseTexture(m_RenderGraphBloomMipUp[i], IBaseRenderGraphBuilder.AccessFlags.ReadWrite);
            }

            builder.SetRenderFunc(static (BloomPassData data, LowLevelGraphContext context) =>
            {
                var cmd = context.legacyCmd;
                var material = data.material;
                var mipCount = data.mipCount;

                var loadAction = RenderBufferLoadAction.DontCare;
                var storeAction = RenderBufferStoreAction.Store;

                // Prefilter
                using (new ProfilingScope(cmd, Profiling.s_BloomPrefilter))
                {
                    Blitter.BlitCameraTexture(cmd, data.sourceTexture, data.bloomMipDown[0], loadAction, storeAction, material, 0);
                }

                // Downsample
                using (new ProfilingScope(cmd, Profiling.s_BloomDownsample))
                {
                    TextureHandle lastDown = data.bloomMipDown[0];
                    for (int i = 1; i < mipCount; i++)
                    {
                        TextureHandle mipDown = data.bloomMipDown[i];
                        TextureHandle mipUp = data.bloomMipUp[i];

                        Blitter.BlitCameraTexture(cmd, lastDown, mipUp, loadAction, storeAction, material, 1);
                        Blitter.BlitCameraTexture(cmd, mipUp, mipDown, loadAction, storeAction, material, 2);

                        lastDown = mipDown;
                    }
                }

                // Up sample
                using (new ProfilingScope(cmd, Profiling.s_BloomUpsample))
                {
                    // Upsample (bilinear by default, HQ filtering does bicubic instead
                    for (int i = mipCount - 2; i >= 0; i--)
                    {
                        TextureHandle lowMip = (i == mipCount - 2) ? data.bloomMipDown[i + 1] : data.bloomMipUp[i + 1];
                        TextureHandle highMip = data.bloomMipDown[i];
                        TextureHandle dst = data.bloomMipUp[i];

                        // We need a separate material for each upsample pass because setting the low texture mip source
                        // gets overriden by the time the render func is executed.
                        // Material is a reference, so all the blits would share the same material state in the cmdbuf.
                        // NOTE: another option would be to use cmd.SetGlobalTexture().
                        var upMaterial = data.upsampleMaterials[i];
                        upMaterial.SetTexture(ShaderConstants._SourceTexLowMip, lowMip);

                        Blitter.BlitCameraTexture(cmd, highMip, dst, loadAction, storeAction, upMaterial, 3);
                    }
                }
            });

            destination = passData.bloomMipUp[0];
        }
    }

    private void SetupRenderGraphUberPassBloom(RenderGraph renderGraph, in TextureHandle bloomTexture, Material uberMaterial)
    {
        // Set global bloom texture and enable bloom in uber pass
        using (var builder = renderGraph.AddRasterRenderPass<BloomPassData>(Profiling.s_UberPassSetupBloom.name, out var passData, Profiling.s_UberPassSetupBloom))
        {
            passData.sourceTexture = builder.UseTexture(bloomTexture, IBaseRenderGraphBuilder.AccessFlags.Read);
            passData.material = uberMaterial;
            passData.bloomIntensity = m_Bloom.intensity;

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((BloomPassData data, RasterGraphContext rasterGraphContext) =>
            {
                var material = data.material;
                material.EnableKeyword(ShaderKeywordStrings.Bloom);
                material.SetFloat(ShaderConstants._BloomIntensity, data.bloomIntensity);
                rasterGraphContext.cmd.SetGlobalTexture(ShaderConstants._Bloom_Texture, data.sourceTexture);
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
        using (var builder = renderGraph.AddRasterRenderPass<UberPassData>(Profiling.s_ApplyPostProcessing.name, out var passData, Profiling.s_ApplyPostProcessing))
        {
            if (m_Bloom.IsActive() && m_RenderGraphBloomMipUp[0].IsValid())
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
        public readonly Material[] bloomUpsample;

        public MaterialLibrary(PostProcessingData data)
        {
            uberPost = CoreUtils.CreateEngineMaterial(data.shaders.uberPostShader);
            bloom = CoreUtils.CreateEngineMaterial(data.shaders.bloomShader);

            bloomUpsample = new Material[k_MaxPyramidSize];
            for (uint i = 0; i < k_MaxPyramidSize; ++i)
                bloomUpsample[i] = CoreUtils.CreateEngineMaterial(data.shaders.bloomShader);
        }

        public void Cleanup()
        {
            CoreUtils.Destroy(uberPost);
            CoreUtils.Destroy(bloom);

            for (uint i = 0; i < k_MaxPyramidSize; ++i)
                CoreUtils.Destroy(bloomUpsample[i]);
        }
    }
}
