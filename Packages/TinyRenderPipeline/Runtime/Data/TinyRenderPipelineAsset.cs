using System;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/Tiny Render Pipeline Asset")]
public class TinyRenderPipelineAsset : RenderPipelineAsset
{
    public enum ShadowResolution
    {
        _1024 = 1024,
        _2048 = 2048,
        _4096 = 4096
    }

    [Serializable]
    private class MainLightShadow
    {
        public ShadowResolution shadowResolution = ShadowResolution._4096;

        [Range(1, 4)]
        public int cascadeCount = 4;

        [Range(0.0f, 1.0f)]
        public float cascadeRatio1 = 0.067f, cascadeRatio2 = 0.2f, cascadeRatio3 = 0.467f;
    }

    [Serializable]
    private class AdditionalLightsShadow
    {
        public ShadowResolution shadowResolution = ShadowResolution._2048;
    }

    // Shadows
    [Serializable]
    private class Shadows
    {
        public float shadowDistance = 50.0f;

        [Range(0.0f, 1.0f)]
        public float cascadeBorder = 0.2f;

        public MainLightShadow mainLightShadow = default;

        public AdditionalLightsShadow additionalLightsShadow = default;
    }

    [SerializeField]
    private bool m_UseSRPBatcher = true;

    [SerializeField]
    private bool m_SupportsHDR = true;

    [SerializeField]
    private Shadows m_Shadows = default;

    [SerializeField]
    private PostProcessingData m_PostProcessingData = default;

    public bool useSRPBatcher
    {
        get { return m_UseSRPBatcher; }
        set { m_UseSRPBatcher = value; }
    }

    public bool supportsHDR
    {
        get { return m_SupportsHDR; }
        set { m_SupportsHDR = value; }
    }

    public float shadowDistance
    {
        get { return m_Shadows.shadowDistance; }
        set { m_Shadows.shadowDistance = Mathf.Max(0.0f, value); }
    }

    public int mainLightShadowmapResolution
    {
        get { return (int)m_Shadows.mainLightShadow.shadowResolution; }
        set { m_Shadows.mainLightShadow.shadowResolution = (ShadowResolution)value; }
    }

    public int cascadesCount => m_Shadows.mainLightShadow.cascadeCount;

    public Vector3 cascadesSplit => new Vector3(m_Shadows.mainLightShadow.cascadeRatio1, m_Shadows.mainLightShadow.cascadeRatio2, m_Shadows.mainLightShadow.cascadeRatio3);

    public float cascadeBorder => m_Shadows.cascadeBorder;

    public int additionalLightsShadowmapResolution
    {
        get { return (int)m_Shadows.additionalLightsShadow.shadowResolution; }
        set { m_Shadows.additionalLightsShadow.shadowResolution = (ShadowResolution)value; }
    }

    public PostProcessingData postProcessingData => m_PostProcessingData;

    public TinyRenderPipeline renderPipeline;

    public override Type pipelineType => renderPipeline.GetType();

    protected override RenderPipeline CreatePipeline()
    {
        renderPipeline = new TinyRenderPipeline(this);
        return renderPipeline;
    }
}
