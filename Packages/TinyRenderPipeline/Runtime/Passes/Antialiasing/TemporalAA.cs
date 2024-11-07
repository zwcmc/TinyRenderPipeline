using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class TemporalAA
{
    private static readonly ProfilingSampler s_TaaSampler = new ProfilingSampler("Temporal AA");
    private static readonly ProfilingSampler s_TaaHistorySampler = new ProfilingSampler("Temporal AA History");

    private const string k_TaaHistoryTextureName = "_TaaHistoryTexture";

    private static readonly int s_HaltonSampleCount = 16;
    private static Vector2[] s_Halton23Samples = new Vector2[s_HaltonSampleCount];

    private static int s_LastFrameIndex;
    // Last frame non-jittered vp matrix
    private static Matrix4x4 s_LastFrameViewProjection;
    // Current frame non-jittered vp matrix
    private static Matrix4x4 s_CurrentFrameViewProjection;
    // Current frame jitter
    private static Vector2 s_Jitter;

    private RTHandle m_TaaHistoryRTHandle;

    private Vector4 m_BlitScaleBias;

    private static class TaaMaterialParamShaderIDs
    {
        public static readonly int HistoryColorTexture = Shader.PropertyToID(k_TaaHistoryTextureName);
        public static readonly int TaaFeedback = Shader.PropertyToID("_TaaFeedback");
        public static readonly int HistoryReprojection = Shader.PropertyToID("_HistoryReprojection");
        public static readonly int TaaFilterWeights = Shader.PropertyToID("_TaaFilterWeights");
        public static readonly int TaaVarianceGamma = Shader.PropertyToID("_TaaVarianceGamma");
    }

    private Material m_TaaMaterial;

    private class PassData
    {
        public TextureHandle input;
        public TextureHandle history;
        public Material taaMaterial;
    }

    private static class TaaSettings
    {
        public static float filterWidth = 1.0f;    // Reconstruction filter width typically between 0.2 (sharper, aliased) and 1.5 (smoother)
        public static float feedback = 0.16f;      // History feedback, between 0 (maximum temporal AA) and 1 (no temporal AA).
        public static float varianceGamma = 1.0f;  // High values increases ghosting artefact, lower values increases jittering, range [0.75, 1.25]
    }

    private static readonly Vector2[] s_SamplesOffset = new Vector2[]
    {
        new Vector2(-1.0f, -1.0f), new Vector2(0.0f, -1.0f), new Vector2(1.0f, -1.0f),
        new Vector2(-1.0f, 0.0f),  new Vector2(0.0f, 0.0f),  new Vector2(1.0f, 0.0f),
        new Vector2(-1.0f, 1.0f),  new Vector2(0.0f, 1.0f),  new Vector2(1.0f, 1.0f)
    };

    public TemporalAA(Shader shader)
    {
        if (shader)
            m_TaaMaterial = CoreUtils.CreateEngineMaterial(shader);

        for (int i = 0; i < s_HaltonSampleCount; ++i)
        {
            s_Halton23Samples[i].x = Halton(i, 2);
            s_Halton23Samples[i].y = Halton(i, 3);
        }

        // Reset variables
        s_LastFrameIndex = -1;
        s_LastFrameViewProjection = Matrix4x4.identity;
        s_CurrentFrameViewProjection = Matrix4x4.identity;
        s_Jitter = Vector2.zero;

        m_BlitScaleBias = new Vector4(1.0f, 1.0f, 0.0f, 0.0f);
    }

    public void Dispose()
    {
        m_TaaHistoryRTHandle?.Release();
        CoreUtils.Destroy(m_TaaMaterial);
    }

    public static void TaaJitterProjectionMatrix(in RenderTextureDescriptor cameraDescriptor, in Matrix4x4 viewMatrix, ref Matrix4x4 projectionMatrix)
    {
        int frameIndex = Time.frameCount;

        s_Jitter = s_Halton23Samples[frameIndex % s_HaltonSampleCount];

        var gpuProjectionMatrix = viewMatrix * GL.GetGPUProjectionMatrix(projectionMatrix, true);

        if (s_LastFrameIndex == -1)
        {
            s_LastFrameViewProjection = gpuProjectionMatrix;
            s_CurrentFrameViewProjection = gpuProjectionMatrix;
        }

        s_LastFrameViewProjection = s_CurrentFrameViewProjection;
        s_CurrentFrameViewProjection = gpuProjectionMatrix;

        float cameraTargetWidth = (float)cameraDescriptor.width;
        float cameraTargetHeight = (float)cameraDescriptor.height;
        projectionMatrix.m02 -= s_Jitter.x * (2.0f / cameraTargetWidth);
        projectionMatrix.m12 -= s_Jitter.y * (2.0f / cameraTargetHeight);

        s_LastFrameIndex = frameIndex;
    }

    public void RecordRenderGraph(RenderGraph renderGraph, in TextureHandle currentColorTexture, ref TextureHandle target, ref RenderingData renderingData)
    {
        if (m_TaaMaterial == null)
        {
            Debug.LogError("TAA Pass: TAA material is null.");
            return;
        }

        bool isHistoryValid = m_TaaHistoryRTHandle != null && m_TaaHistoryRTHandle.rt != null;
        TextureHandle history = isHistoryValid ? renderGraph.ImportTexture(m_TaaHistoryRTHandle) : currentColorTexture;
        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_TaaSampler.name, out var passData, s_TaaSampler))
        {
            passData.input = currentColorTexture;
            builder.UseTexture(currentColorTexture, IBaseRenderGraphBuilder.AccessFlags.Read);

            passData.history = builder.UseTexture(history, IBaseRenderGraphBuilder.AccessFlags.Read);

            passData.taaMaterial = m_TaaMaterial;

            m_TaaMaterial.SetFloat(TaaMaterialParamShaderIDs.TaaFeedback, TaaSettings.feedback);

            Matrix4x4 historyViewProjection = isHistoryValid ? s_LastFrameViewProjection : s_CurrentFrameViewProjection;
            Matrix4x4 normalizedToClip = Matrix4x4.identity;
            normalizedToClip.m00 = 2.0f;
            normalizedToClip.m03 = -1.0f;
            normalizedToClip.m11 = 2.0f;
            normalizedToClip.m13 = -1.0f;
            Matrix4x4 historyReprojection = historyViewProjection * Matrix4x4.Inverse(s_CurrentFrameViewProjection) * normalizedToClip;
            m_TaaMaterial.SetMatrix(TaaMaterialParamShaderIDs.HistoryReprojection, historyReprojection);

            float[] weights = new float[9];
            ComputeWeights(ref weights);
            m_TaaMaterial.SetFloatArray(TaaMaterialParamShaderIDs.TaaFilterWeights, weights);

            m_TaaMaterial.SetFloat(TaaMaterialParamShaderIDs.TaaVarianceGamma, TaaSettings.varianceGamma);

            builder.UseTextureFragment(target, 0, IBaseRenderGraphBuilder.AccessFlags.WriteAll);

            builder.AllowPassCulling(false);

            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
            {
                data.taaMaterial.SetTexture(TaaMaterialParamShaderIDs.HistoryColorTexture, data.history);
                Blitter.BlitTexture(context.cmd, data.input, m_BlitScaleBias, data.taaMaterial, 0);
            });
        }

        if (!isHistoryValid)
        {
            var colorDescriptor = renderingData.cameraTargetDescriptor;
            colorDescriptor.depthStencilFormat = GraphicsFormat.None;
            RenderingUtils.ReAllocateIfNeeded(ref m_TaaHistoryRTHandle, colorDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: k_TaaHistoryTextureName);
        }

        // Store TAA history
        TextureHandle destTexture = renderGraph.ImportTexture(m_TaaHistoryRTHandle);
        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_TaaHistorySampler.name, out var passData, s_TaaHistorySampler))
        {
            passData.input = target;
            builder.UseTexture(target, IBaseRenderGraphBuilder.AccessFlags.Read);

            passData.taaMaterial = m_TaaMaterial;

            builder.UseTextureFragment(destTexture, 0, IBaseRenderGraphBuilder.AccessFlags.WriteAll);

            builder.AllowPassCulling(false);

            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
            {
                Blitter.BlitTexture(context.cmd, data.input, m_BlitScaleBias, data.taaMaterial, 1);
            });
        }
    }

    // This is a gaussian fit of a 3.3-wide Blackman-Harris window
    // see: "High Quality Temporal Supersampling" by Brian Karis
    private static void ComputeWeights(ref float[] weights)
    {
        int frameIndex = Time.frameCount;
        Vector2 jitter = s_Halton23Samples[frameIndex % s_HaltonSampleCount];
        float totalWeight = 0.0f;
        for (int i = 0; i < 9; ++i)
        {
            Vector2 o = s_SamplesOffset[i];
            Vector2 d = (o - jitter) / TaaSettings.filterWidth;
            float d2 = d.x * d.x + d.y * d.y;
            weights[i] = Mathf.Exp((-0.5f / (0.22f)) * d2);
            totalWeight += weights[i];
        }

        // Normalize weights.
        for (int i = 0; i < 9; ++i)
        {
            weights[i] /= totalWeight;
        }
    }

    private static float Halton(int i, int b)
    {
        // 跳过序列前面的元素使得生成出的序列元素的平均值更接近 0.5
        i += 409;

        float f = 1.0f;
        float r = 0.0f;
        while (i > 0)
        {
            f /= (float)b;
            r += f * (float)(i % b);
            i /= b;
        }
        return r;
    }
}
