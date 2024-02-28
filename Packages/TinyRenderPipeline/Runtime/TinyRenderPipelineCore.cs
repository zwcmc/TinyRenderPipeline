using UnityEngine;
using UnityEngine.Rendering;

public struct ShadowData
{
    public int mainLightShadowmapWidth;
    public int mainLightShadowmapHeight;

    public int cascadesCount;
    public Vector3 cascadesSplit;

    // max shadowing distance
    public float maxShadowDistance;

    // Main light last cascade shadow fade border
    public float mainLightShadowCascadeBorder;
}

public struct RenderingData
{
    internal ScriptableRenderContext renderContext;
    internal CommandBuffer commandBuffer;
    public Camera camera;
    public CullingResults cullResults;

    public int mainLightIndex;
    public int additionalLightsCount;

    public ShadowData shadowData;

    public PerObjectData perObjectData;
}
