using UnityEngine;
using UnityEngine.Rendering;

public struct ShadowData
{
    public int mainLightShadowmapWidth;
    public int mainLightShadowmapHeight;

    public int cascadesCount;
    public Vector3 cascadesSplit;
}

public struct RenderingData
{
    internal ScriptableRenderContext renderContext;
    internal CommandBuffer commandBuffer;
    public Camera camera;
    public CullingResults cullResults;
    public int mainLightIndex;

    public ShadowData shadowData;
}
