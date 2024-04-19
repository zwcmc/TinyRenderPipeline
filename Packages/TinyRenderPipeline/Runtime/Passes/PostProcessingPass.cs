using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class PostProcessingPass
{
    private static class Profiling
    {
        public static readonly ProfilingSampler renderPostProcessing = new ProfilingSampler("Render PostProcessing Effects");
        public static readonly ProfilingSampler uberPostProcessing = new ProfilingSampler("UberPostProcess");
        public static readonly ProfilingSampler bloom = new ProfilingSampler("Bloom");
    }

    private static RTHandle k_CameraTarget = RTHandles.Alloc(BuiltinRenderTextureType.CameraTarget);

    private Material m_PostProcessingMaterial;

    private RenderTextureDescriptor m_Descriptor;
    private RTHandle m_Source;
    private RTHandle m_Destination;

    private bool m_ResolveToScreen;

    private RTHandle m_InternalLut;

    private MaterialLibrary m_Materials;

    // Post processing effects settings
    private PostProcessingData.Bloom m_Bloom;

    // Bloom
    private const int k_MaxPyramidSize = 16;
    private GraphicsFormat m_DefaultHDRFormat;
    private RTHandle[] m_BloomMipDown;
    private RTHandle[] m_BloomMipUp;

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

    private enum BloomPass
    {
        BloomPrefilter = 0,
        BloomBlurH,
        BloomBlurV,
        BloomUpsample,
    }

    public PostProcessingPass()
    {
        ShaderConstants._BloomMipUp = new int[k_MaxPyramidSize];
        ShaderConstants._BloomMipDown = new int[k_MaxPyramidSize];
        m_BloomMipUp = new RTHandle[k_MaxPyramidSize];
        m_BloomMipDown = new RTHandle[k_MaxPyramidSize];
        for (int i = 0; i < k_MaxPyramidSize; ++i)
        {
            ShaderConstants._BloomMipUp[i] = Shader.PropertyToID("_BloomMipUp" + i);
            ShaderConstants._BloomMipDown[i] = Shader.PropertyToID("_BloomMipDown" + i);

            m_BloomMipUp[i] = RTHandles.Alloc(ShaderConstants._BloomMipUp[i], name: "_BloomMipUp" + i);
            m_BloomMipDown[i] = RTHandles.Alloc(ShaderConstants._BloomMipDown[i], name: "_BloomMipDown" + i);
        }
    }

    public void Setup(in RenderTextureDescriptor baseDescriptor, in RTHandle source, bool resolveToScreen, in RTHandle internalLut, PostProcessingData postProcessingData)
    {
        m_PostProcessingData = postProcessingData;
        if (m_PostProcessingData != null)
        {
            m_Materials = new MaterialLibrary(m_PostProcessingData);
        }

        m_Descriptor = baseDescriptor;
        m_Descriptor.useMipMap = false;
        m_Descriptor.autoGenerateMips = false;
        m_Source = source;
        m_Destination = k_CameraTarget;
        m_ResolveToScreen = resolveToScreen;

        m_InternalLut = internalLut;
    }

    public void ExecutePass(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (m_Materials == null)
        {
            Debug.LogError("Post Processing Pass: post-processing materials is null.");
            return;
        }

        if (m_PostProcessingData == null)
        {
            Debug.LogError("Post Processing Pass: post-processing data is null.");
            return;
        }

        m_Bloom = m_PostProcessingData.bloom;

        m_DefaultHDRFormat = renderingData.isHdrEnabled ? SystemInfo.GetGraphicsFormat(DefaultFormat.HDR) : SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);

        var cmd = renderingData.commandBuffer;
        using (new ProfilingScope(cmd, Profiling.renderPostProcessing))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            Render(cmd, ref renderingData);
        }
    }

    public void Dispose()
    {
        foreach (var handle in m_BloomMipUp)
            handle?.Release();
        foreach (var handle in m_BloomMipDown)
            handle?.Release();

        m_Materials?.Cleanup();
    }

    private void Render(CommandBuffer cmd, ref RenderingData renderingData)
    {
        RTHandle source = m_Source;

        using (new ProfilingScope(cmd, Profiling.uberPostProcessing))
        {
            // Reset uber keywords
            m_Materials.uberPost.shaderKeywords = null;

            if (m_Bloom.IsActive())
            {
                // Bloom
                using (new ProfilingScope(cmd, Profiling.bloom))
                    SetupBloom(cmd, source, m_Materials.uberPost);
            }

            // Color grading
            SetupColorGrading(ref renderingData, m_Materials.uberPost);

            if (m_ResolveToScreen)
                RenderingUtils.FinalBlit(cmd, renderingData.camera, source, m_Destination, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, m_Materials.uberPost, 0);
        }
    }

    #region Bloom

    private void SetupBloom(CommandBuffer cmd, RTHandle source, Material uberMaterial)
    {
        // Start at half-res
        int downres = 1;
        switch (m_Bloom.downscale)
        {
            case PostProcessingData.BloomDownscaleMode.Half:
                downres = 1;
                break;
            case PostProcessingData.BloomDownscaleMode.Quarter:
                downres = 2;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        int tw = m_Descriptor.width >> downres;
        int th = m_Descriptor.height >> downres;

        // Determine the iteration count
        int maxSize = Mathf.Max(tw, th);
        int iterations = Mathf.FloorToInt(Mathf.Log(maxSize, 2f) - 1);
        int mipCount = Mathf.Clamp(iterations, 1, m_Bloom.maxIterations);

        // Pre-filtering parameters
        float clamp = m_Bloom.clamp;
        float threshold = Mathf.GammaToLinearSpace(m_Bloom.threshold);
        float thresholdKnee = threshold * 0.5f;

        // Material setup
        float scatter = Mathf.Lerp(0.05f, 0.95f, m_Bloom.scatter);
        var bloomMaterial = m_Materials.bloom;
        bloomMaterial.SetVector(ShaderConstants._BloomParams, new Vector4(scatter, clamp, threshold, thresholdKnee));
        CoreUtils.SetKeyword(bloomMaterial, ShaderKeywordStrings.BloomHQ, m_Bloom.highQualityFiltering);
        bloomMaterial.SetFloat(ShaderConstants._BloomIntensity, m_Bloom.intensity);

        // Prefilter
        var desc = RenderingUtils.GetCompatibleDescriptor(m_Descriptor, tw, th, m_DefaultHDRFormat);
        for (int i = 0; i < mipCount; i++)
        {
            RenderingUtils.ReAllocateIfNeeded(ref m_BloomMipUp[i], desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: m_BloomMipUp[i].name);
            RenderingUtils.ReAllocateIfNeeded(ref m_BloomMipDown[i], desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: m_BloomMipDown[i].name);
            desc.width = Mathf.Max(1, desc.width >> 1);
            desc.height = Mathf.Max(1, desc.height >> 1);
        }
        Blitter.BlitCameraTexture(cmd, source, m_BloomMipDown[0], RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, bloomMaterial, (int)BloomPass.BloomPrefilter);

        // Downsample - gaussian pyramid
        var lastDown = m_BloomMipDown[0];
        for (int i = 1; i < mipCount; i++)
        {
            Blitter.BlitCameraTexture(cmd, lastDown, m_BloomMipUp[i], RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, bloomMaterial, (int)BloomPass.BloomBlurH);
            Blitter.BlitCameraTexture(cmd, m_BloomMipUp[i], m_BloomMipDown[i], RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, bloomMaterial, (int)BloomPass.BloomBlurV);

            lastDown = m_BloomMipDown[i];
        }

        // Upsample (bilinear by default, HQ filtering does bicubic instead
        for (int i = mipCount - 2; i >= 0; i--)
        {
            var lowMip = (i == mipCount - 2) ? m_BloomMipDown[i + 1] : m_BloomMipUp[i + 1];
            var highMip = m_BloomMipDown[i];
            var dst = m_BloomMipUp[i];

            cmd.SetGlobalTexture(ShaderConstants._SourceTexLowMip, lowMip);
            Blitter.BlitCameraTexture(cmd, highMip, dst, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, bloomMaterial, (int)BloomPass.BloomUpsample);
        }

        // Setup bloom on uber post
        uberMaterial.SetFloat(ShaderConstants._BloomIntensity, m_Bloom.intensity);
        cmd.SetGlobalTexture(ShaderConstants._Bloom_Texture, m_BloomMipUp[0]);
        uberMaterial.EnableKeyword(ShaderKeywordStrings.Bloom);
    }

    #endregion

    #region Color Grading

    private void SetupColorGrading(ref RenderingData renderingData, Material uberMaterial)
    {
        int lutHeight = renderingData.lutSize;
        int lutWidth = lutHeight * lutHeight;

        uberMaterial.SetTexture(ShaderConstants._InternalLut, m_InternalLut);
        uberMaterial.SetVector(ShaderConstants._Lut_Params, new Vector4(1f / lutWidth, 1f / lutHeight, lutHeight - 1f, 1f));

        if (renderingData.isHdrEnabled)
            uberMaterial.EnableKeyword(ShaderKeywordStrings.HDRColorGrading);
    }

    #endregion

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
