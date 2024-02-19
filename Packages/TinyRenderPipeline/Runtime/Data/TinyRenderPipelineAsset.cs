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
    private struct Shadows
    {
        public float shadowDistance;
        public ShadowResolution shadowResolution;
    }

    [SerializeField] private bool m_UseSRPBatcher = true;
    [SerializeField] private Shadows m_MainLightShadow = new Shadows
    {
        shadowDistance = 50.0f,
        shadowResolution = ShadowResolution._2048
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

    public override Type pipelineType => renderPipeline.GetType();

    protected override RenderPipeline CreatePipeline()
    {
        renderPipeline = new TinyRenderPipeline(this);
        return renderPipeline;
    }
}
