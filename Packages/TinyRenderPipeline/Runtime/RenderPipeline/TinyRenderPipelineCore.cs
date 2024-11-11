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

public enum SoftShadows
{
    OFF = 0,
    PCF = 1,
    PCSS = 2
}

public enum AntialiasingMode
{
    [InspectorName("Off")]
    Off,

    [InspectorName("Fast Approximate Anti-aliasing (FXAA)")]
    FastApproximateAntialiasing,

    [InspectorName("Temporal Anti-aliasing (TAA)")]
    TemporalAntialiasing
}

public struct ShadowData
{
    public bool mainLightShadowsEnabled;
    public int mainLightShadowMapWidth;
    public int mainLightShadowMapHeight;
    public int cascadesCount;
    public Vector3 cascadesSplit;
    // max shadowing distance
    public float maxShadowDistance;
    // Main light last cascade shadow fade border
    public float mainLightShadowCascadeBorder;

    public SoftShadows softShadows;

    public bool additionalLightsShadowEnabled;
    public int additionalLightsShadowMapWidth;
    public int additionalLightsShadowMapHeight;
}

public struct CameraData
{
    public Camera camera;
    public GraphicsFormat defaultGraphicsFormat;
    public RenderTextureDescriptor targetDescriptor;
    public Vector3 worldSpaceCameraPos;
}

public struct RenderingData
{
    public ScriptableRenderContext renderContext;
    public CommandBuffer commandBuffer;

    public CameraData cameraData;

    // public bool isDefaultCameraViewport;

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

    public AntialiasingMode antialiasing;
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

    public const string ShadowPCF = "_SHADOWS_PCF";

    public const string ShadowPCSS = "_SHADOWS_PCSS";
}

public static class ShaderPropertyIDs
{
    public static readonly int WorldSpaceCameraPos = Shader.PropertyToID("_WorldSpaceCameraPos");
    public static readonly int ZBufferParams = Shader.PropertyToID("_ZBufferParams");
    public static readonly int OrthoParams = Shader.PropertyToID("unity_OrthoParams");
    public static readonly int ProjectionParams = Shader.PropertyToID("_ProjectionParams");
    public static readonly int ScreenParams = Shader.PropertyToID("_ScreenParams");
}

public enum StencilUsage
{
    Clear = 0,
    ScreenSpaceReflection = (1 << 3),
}
