using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class TemporalAA
{
    private static readonly ProfilingSampler s_TaaSampler = new ProfilingSampler("Temporal AA");
    private static readonly ProfilingSampler s_TaaHistorySampler = new ProfilingSampler("Temporal AA History");

    private const string k_TaaHistoryTextureName = "_TaaHistoryTexture";

    private RTHandle m_TaaHistoryRTHandle;

    private Vector4 m_BlitScaleBias;

    private static class TaaMaterialParamShaderIDs
    {
        public static readonly int HistoryColorTexture = Shader.PropertyToID(k_TaaHistoryTextureName);
        public static readonly int HistoryReprojection = Shader.PropertyToID("_HistoryReprojection");
        public static readonly int TaaFilterWeights = Shader.PropertyToID("_TaaFilterWeights");
        public static readonly int TaaFrameInfo = Shader.PropertyToID("_TaaFrameInfo");
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
        public static float alpha = 0.1f;         // History feedback, between 0 (maximum temporal AA) and 1 (no temporal AA).
        public static float stDevScale = 2.16f;
    }

    private static readonly Vector2[] s_SamplesOffset =
    {
        new Vector2(-1.0f, -1.0f), new Vector2(0.0f, -1.0f), new Vector2(1.0f, -1.0f),
        new Vector2(-1.0f, 0.0f),  new Vector2(0.0f, 0.0f),  new Vector2(1.0f, 0.0f),
        new Vector2(-1.0f, 1.0f),  new Vector2(0.0f, 1.0f),  new Vector2(1.0f, 1.0f)
    };

    public TemporalAA(Shader shader)
    {
        if (shader)
            m_TaaMaterial = CoreUtils.CreateEngineMaterial(shader);

        m_BlitScaleBias = new Vector4(1.0f, 1.0f, 0.0f, 0.0f);
    }

    public void Dispose()
    {
        m_TaaHistoryRTHandle?.Release();
        CoreUtils.Destroy(m_TaaMaterial);
    }

    public void RecordRenderGraph(RenderGraph renderGraph, in TextureHandle currentColorTexture, ref TextureHandle target, ref RenderingData renderingData)
    {
        if (m_TaaMaterial == null)
        {
            Debug.LogError("TAA Pass: TAA material is null.");
            return;
        }

        bool isFirstFrame = m_TaaHistoryRTHandle == null || m_TaaHistoryRTHandle.rt == null;
        TextureHandle history = isFirstFrame ? currentColorTexture : renderGraph.ImportTexture(m_TaaHistoryRTHandle);
        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_TaaSampler.name, out var passData, s_TaaSampler))
        {
            passData.input = builder.UseTexture(currentColorTexture, IBaseRenderGraphBuilder.AccessFlags.Read);
            passData.history = builder.UseTexture(history, IBaseRenderGraphBuilder.AccessFlags.Read);

            passData.taaMaterial = m_TaaMaterial;

            Matrix4x4 currentFrameGpuVP = GL.GetGPUProjectionMatrix(FrameHistory.GetCurrentFrameProjection(), true) * FrameHistory.GetCurrentFrameView();
            Matrix4x4 historyViewProjection = GL.GetGPUProjectionMatrix(FrameHistory.GetLastFrameProjection(), true) * FrameHistory.GetLastFrameView();
            Matrix4x4 normalizedToClip = Matrix4x4.identity;
            normalizedToClip.m00 = 2.0f;
            normalizedToClip.m03 = -1.0f;
            normalizedToClip.m11 = 2.0f;
            normalizedToClip.m13 = -1.0f;
            Matrix4x4 historyReprojection = historyViewProjection * Matrix4x4.Inverse(currentFrameGpuVP) * normalizedToClip;
            m_TaaMaterial.SetMatrix(TaaMaterialParamShaderIDs.HistoryReprojection, historyReprojection);

            float[] weights = new float[9];
            ComputeWeights(ref weights);
            m_TaaMaterial.SetFloatArray(TaaMaterialParamShaderIDs.TaaFilterWeights, weights);

            Vector4 taaFrameInfo = new Vector4(TaaSettings.alpha, TaaSettings.stDevScale, isFirstFrame ? 1.0f : 0.0f, 0.0f);
            m_TaaMaterial.SetVector(TaaMaterialParamShaderIDs.TaaFrameInfo, taaFrameInfo);

            builder.UseTextureFragment(target, 0, IBaseRenderGraphBuilder.AccessFlags.WriteAll);

            builder.AllowPassCulling(false);

            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
            {
                data.taaMaterial.SetTexture(TaaMaterialParamShaderIDs.HistoryColorTexture, data.history);
                Blitter.BlitTexture(context.cmd, data.input, m_BlitScaleBias, data.taaMaterial, 0);
            });
        }

        if (isFirstFrame)
        {
            var colorDescriptor = renderingData.cameraData.targetDescriptor;
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
        Vector2 jitter = FrameHistory.TaaJitter;
        float totalWeight = 0.0f;
        for (int i = 0; i < 9; ++i)
        {
            Vector2 o = s_SamplesOffset[i];
            Vector2 d = (o - jitter) / TaaSettings.filterWidth;
            float d2 = d.x * d.x + d.y * d.y;
            weights[i] = Mathf.Exp(-2.29f * d2);
            totalWeight += weights[i];
        }

        // Normalize weights.
        for (int i = 0; i < 9; ++i)
        {
            weights[i] /= totalWeight;
        }
    }
}
