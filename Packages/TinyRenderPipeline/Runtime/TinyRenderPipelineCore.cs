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
    [InspectorName("No Anti-aliasing")]
    None,

    [InspectorName("Fast Approximate Anti-aliasing (FXAA)")]
    FastApproximateAntialiasing
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

public struct RenderingData
{
    public ScriptableRenderContext renderContext;
    public CommandBuffer commandBuffer;

    public Camera camera;

    public GraphicsFormat defaultFormat;

    public RenderTextureDescriptor cameraTargetDescriptor;

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

public static class ShaderPropertyID
{
    public static readonly int worldSpaceCameraPos = Shader.PropertyToID("_WorldSpaceCameraPos");
    public static readonly int zBufferParams = Shader.PropertyToID("_ZBufferParams");
    public static readonly int orthoParams = Shader.PropertyToID("unity_OrthoParams");
    public static readonly int projectionParams = Shader.PropertyToID("_ProjectionParams");
    public static readonly int screenParams = Shader.PropertyToID("_ScreenParams");
    public static readonly int sourceSize = Shader.PropertyToID("_SourceSize");
}
