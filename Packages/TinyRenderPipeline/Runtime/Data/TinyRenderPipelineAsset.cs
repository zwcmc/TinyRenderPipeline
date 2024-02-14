using System;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/Tiny Render Pipeline Asset")]
public class TinyRenderPipelineAsset : RenderPipelineAsset
{
    public TinyRenderPipeline renderPipeline;

    [SerializeField] bool m_UseSRPBatcher = true;

    public bool useSRPBatcher
    {
        get { return m_UseSRPBatcher; }
        set { m_UseSRPBatcher = value; }
    }

    public override Type pipelineType => renderPipeline.GetType();

    protected override RenderPipeline CreatePipeline()
    {
        renderPipeline = new TinyRenderPipeline(this);
        return renderPipeline;
    }
}
