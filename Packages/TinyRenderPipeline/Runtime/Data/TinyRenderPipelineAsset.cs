using System;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/Tiny Render Pipeline Asset")]
public class TinyRenderPipelineAsset : RenderPipelineAsset
{
    public enum ShadowResolution
    {
        _256 = 256,
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
        _4096 = 4096
    }

    public TinyRenderPipeline renderPipeline;

    [Serializable]
    private struct MainLightShadow
    {
        public float shadowDistance;
        public ShadowResolution shadowResolution;

        [Range(1, 4)] public int cascadeCount;
        [Range(0.0f, 1.0f)] public float cascadeRatio1, cascadeRatio2, cascadeRatio3;
        [Range(0.0f, 1.0f)] public float cascadeBorder;
    }

    [SerializeField] private bool m_UseSRPBatcher = true;

    [SerializeField] private MainLightShadow m_MainLightShadow = new MainLightShadow
    {
        shadowDistance = 50.0f,
        shadowResolution = ShadowResolution._2048,
        cascadeCount = 4,
        cascadeRatio1 = 0.1f,
        cascadeRatio2 = 0.25f,
        cascadeRatio3 = 0.5f,
        cascadeBorder = 0.2f
    };

    public bool useSRPBatcher
    {
        get { return m_UseSRPBatcher; }
        set { m_UseSRPBatcher = value; }
    }

    public float shadowDistance
    {
        get { return m_MainLightShadow.shadowDistance; }
        set { m_MainLightShadow.shadowDistance = Mathf.Max(0.0f, value); }
    }

    public int mainLightShadowmapResolution
    {
        get { return (int)m_MainLightShadow.shadowResolution; }
        set { m_MainLightShadow.shadowResolution = (ShadowResolution)value; }
    }

    public int cascadesCount => m_MainLightShadow.cascadeCount;

    public Vector3 cascadesSplit => new Vector3(m_MainLightShadow.cascadeRatio1, m_MainLightShadow.cascadeRatio2, m_MainLightShadow.cascadeRatio3);

    public float cascadeBorder => m_MainLightShadow.cascadeBorder;

    public override Type pipelineType => renderPipeline.GetType();

    protected override RenderPipeline CreatePipeline()
    {
        renderPipeline = new TinyRenderPipeline(this);
        return renderPipeline;
    }
}
