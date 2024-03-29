using UnityEngine;
using UnityEngine.Rendering;

public struct ShadowData
{
    public bool mainLightShadowsEnabled;
    public int mainLightShadowmapWidth;
    public int mainLightShadowmapHeight;
    public int cascadesCount;
    public Vector3 cascadesSplit;
    // max shadowing distance
    public float maxShadowDistance;
    // Main light last cascade shadow fade border
    public float mainLightShadowCascadeBorder;

    public bool additionalLightsShadowEnabled;
    public int additionalLightsShadowmapWidth;
    public int additionalLightsShadowmapHeight;
}

public struct RenderingData
{
    internal ScriptableRenderContext renderContext;
    internal CommandBuffer commandBuffer;

    public Camera camera;
    public RenderTextureDescriptor cameraTargetDescriptor;

    public CullingResults cullResults;

    public int mainLightIndex;
    public int additionalLightsCount;

    public ShadowData shadowData;

    public PerObjectData perObjectData;
}

public struct ShadowSliceData
{
    public Matrix4x4 viewMatrix;
    public Matrix4x4 projectionMatrix;
    public Matrix4x4 shadowTransform;
    public int offsetX;
    public int offsetY;
    public int resolution;
    public ShadowSplitData splitData;
}
