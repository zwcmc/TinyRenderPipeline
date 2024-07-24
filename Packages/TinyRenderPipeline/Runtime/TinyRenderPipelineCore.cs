using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

/// <summary>
/// Shadow map resolution.
/// </summary>
public enum ShadowResolution
{
    _1024 = 1024,
    _2048 = 2048,
    _4096 = 4096
}

/// <summary>
/// Rendering path
/// </summary>
public enum RenderPath
{
    /// <summary>
    /// Forward rendering path: Render all objects and lighting in one pass, with a hard limit on the number of lights that can be applied on an object.
    /// </summary>
    Forward,

    /// <summary>
    /// Forward+ rendering path: Render all objects and lighting in one pass using a clustered data structure to access lighting data.
    /// </summary>
    [InspectorName("Forward+")]
    ForwardPlus,
}

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
    public ScriptableRenderContext renderContext;
    public CommandBuffer commandBuffer;

    public TinyRenderer renderer;

    public Camera camera;

    public float renderScale;

    /// <summary>
    /// True if this camera should render to high dynamic range color targets.
    /// </summary>
    public bool isHdrEnabled;

    public GraphicsFormat defaultFormat;

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

    public bool copyDepthTexture;

    public bool copyColorTexture;
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

public static class ShaderPropertyId
{
    public static readonly int worldSpaceCameraPos = Shader.PropertyToID("_WorldSpaceCameraPos");
    public static readonly int zBufferParams = Shader.PropertyToID("_ZBufferParams");
    public static readonly int orthoParams = Shader.PropertyToID("unity_OrthoParams");
    public static readonly int projectionParams = Shader.PropertyToID("_ProjectionParams");
    public static readonly int screenParams = Shader.PropertyToID("_ScreenParams");
    public static readonly int sourceSize = Shader.PropertyToID("_SourceSize");
}
