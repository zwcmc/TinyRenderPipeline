using System;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/Tiny Render Pipeline Asset")]
public class TinyRenderPipelineAsset : RenderPipelineAsset
{
    [Serializable]
    private struct MainLightShadow
    {
        public ShadowResolution shadowResolution;

        [Range(1, 4)]
        public int cascadeCount;

        [Range(0.0f, 1.0f)]
        public float cascadeRatio1, cascadeRatio2, cascadeRatio3;
    }

    [Serializable]
    private struct AdditionalLightsShadow
    {
        public ShadowResolution shadowResolution;
    }

    [Serializable]
    private struct Shadows
    {
        public float shadowDistance;

        [Range(0.0f, 1.0f)]
        public float cascadeBorder;

        public MainLightShadow mainLightShadow;

        public AdditionalLightsShadow additionalLightsShadow;
    }

    [SerializeField]
    private bool m_UseSRPBatcher = true;

    [SerializeField]
    private Shadows m_Shadows = new Shadows
    {
        shadowDistance = 150.0f,
        cascadeBorder = 0.2f,
        mainLightShadow = new MainLightShadow
        {
            shadowResolution = ShadowResolution._2048,
            cascadeCount = 4,
            cascadeRatio1 = 0.067f,
            cascadeRatio2 = 0.2f,
            cascadeRatio3 = 0.467f
        },
        additionalLightsShadow = new AdditionalLightsShadow
        {
            shadowResolution = ShadowResolution._2048
        }
    };

    [SerializeField]
    private PostProcessingSettings m_PostProcessingSettings = default;

    public enum ShadowResolution
    {
        _256 = 256,
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
        _4096 = 4096
    }

    public TinyRenderPipeline renderPipeline;

    public bool useSRPBatcher
    {
        get { return m_UseSRPBatcher; }
        set { m_UseSRPBatcher = value; }
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

    public PostProcessingSettings postProcessingSettings => m_PostProcessingSettings;

    public override Type pipelineType => renderPipeline.GetType();

    protected override RenderPipeline CreatePipeline()
    {
        renderPipeline = new TinyRenderPipeline(this);
        return renderPipeline;
    }
}
