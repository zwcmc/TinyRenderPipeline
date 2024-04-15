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

    /// <summary>
    /// True if this camera should render to high dynamic range color targets.
    /// </summary>
    public bool isHdrEnabled;

    public RenderTextureDescriptor cameraTargetDescriptor;

    public bool isDefaultCameraViewport;

    public CullingResults cullResults;

    public int mainLightIndex;
    public int additionalLightsCount;

    public ShadowData shadowData;

    public PerObjectData perObjectData;

    public PostProcessingData postProcessingData;

    /// <summary>
    /// The size of the color grading Look Up Table (LUT)
    /// </summary>
    public int lutSize;
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

public static class ShaderKeywordStrings
{
    /// <summary>
    /// Keyword used for high quality Bloom.
    /// </summary>
    public const string BloomHQ = "_BLOOM_HQ";

    /// <summary>
    ///  Keyword used for calculating Bloom in uber post.
    /// </summary>
    public const string Bloom = "_BLOOM";

    /// <summary>
    /// Keyword used for ACES Tonemapping in uber post.
    /// </summary>
    public const string TonemapACES = "_TONEMAP_ACES";

    /// <summary>
    /// Keyword used for Neutral Tonemapping in uber post.
    /// </summary>
    public const string TonemapNeutral = "_TONEMAP_NEUTRAL";

    /// <summary>
    /// Keyword used for calculating Look Up Table Color Grading in uber post.
    /// </summary>
    public const string HDRColorGrading = "_HDR_COLORGRADING";
}
