#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
#endif
using System;
using UnityEngine;
using UnityEngine.Rendering;

public class TinyRenderPipelineAsset : RenderPipelineAsset
{
    [Serializable]
    private class MainLightShadow
    {
        public ShadowResolution shadowResolution = ShadowResolution._4096;

        [Range(1, 4)]
        public int cascadeCount = 4;

        [Range(0.0f, 1.0f)]
        public float cascadeRatio1 = 0.067f, cascadeRatio2 = 0.2f, cascadeRatio3 = 0.467f;

        public SoftShadows softShadows = SoftShadows.NONE;
    }

    [Serializable]
    private class AdditionalLightsShadow
    {
        public ShadowResolution shadowResolution = ShadowResolution._2048;
    }

    [Serializable, ReloadGroup]
    public class ShaderResources
    {
        [Reload("Shaders/Utils/Blit.shader")]
        public Shader blitShader;

        [Reload("Shaders/Utils/CopyDepth.shader")]
        public Shader copyDepthShader;

        [Reload("Shaders/Utils/FallbackError.shader")]
        public Shader fallbackErrorShader;
    }

    // Shadows
    [Serializable]
    private class Shadows
    {
        public float shadowDistance = 50.0f;

        [Range(0.0f, 1.0f)]
        public float cascadeBorder = 0.2f;

        public MainLightShadow mainLightShadow = default;

        public AdditionalLightsShadow additionalLightsShadow = default;
    }

    [SerializeField]
    private ShaderResources m_Shaders;

    [SerializeField]
    private bool m_RequireDepthTexture = false;

    [SerializeField]
    private bool m_RequireColorTexture;

    [SerializeField]
    private Shadows m_Shadows = default;

    [SerializeField]
    private PostProcessingData m_PostProcessingData = default;

    [SerializeField]
    [Range(32, 64)]
    private int m_ColorGradingLutSize = 32;

    [SerializeField]
    [Range(0.1f, 2f)]
    private float m_RenderScale = 1f;

    [SerializeField]
    private bool m_UseRenderGraph = false;

    public bool requireDepthTexture
    {
        get { return m_RequireDepthTexture; }
        set { m_RequireDepthTexture = value; }
    }

    public bool requireColorTexture
    {
        get { return m_RequireColorTexture; }
        set { m_RequireColorTexture = value; }
    }

    public float shadowDistance
    {
        get { return m_Shadows.shadowDistance; }
        set { m_Shadows.shadowDistance = Mathf.Max(0.0f, value); }
    }

    public int mainLightShadowmapResolution
    {
        get { return (int)m_Shadows.mainLightShadow.shadowResolution; }
        set { m_Shadows.mainLightShadow.shadowResolution = (ShadowResolution)value; }
    }

    public int cascadesCount
    {
        get { return m_Shadows.mainLightShadow.cascadeCount; }
    }

    public Vector3 cascadesSplit
    {
        get
        {
            return new Vector3(
                m_Shadows.mainLightShadow.cascadeRatio1,
                m_Shadows.mainLightShadow.cascadeRatio2,
                m_Shadows.mainLightShadow.cascadeRatio3
            );
        }
    }

    public float cascadeBorder
    {
        get { return m_Shadows.cascadeBorder; }
    }

    public SoftShadows softShadows => m_Shadows.mainLightShadow.softShadows;

    public int additionalLightsShadowmapResolution
    {
        get { return (int)m_Shadows.additionalLightsShadow.shadowResolution; }
        set { m_Shadows.additionalLightsShadow.shadowResolution = (ShadowResolution)value; }
    }

    public int colorGradingLutSize
    {
        get { return (int)m_ColorGradingLutSize; }
        set { m_ColorGradingLutSize = Mathf.Clamp(value, 32, 64); }
    }

    public float renderScale
    {
        get { return m_RenderScale; }
        set { m_RenderScale = Mathf.Clamp(value, 0.1f, 2f); }
    }

    public bool useRenderGraph
    {
        get { return m_UseRenderGraph; }
        set { m_UseRenderGraph = value; }
    }

    public ShaderResources shaders
    {
        get { return m_Shaders; }
    }

    public PostProcessingData postProcessingData
    {
        get { return m_PostProcessingData; }
    }

    public TinyRenderPipeline renderPipeline;

    public override Type pipelineType => renderPipeline.GetType();

    public static readonly string packagePath = "Packages/com.tiny.render-pipeline";

#if UNITY_EDITOR
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812")]
    private class CreateTinyRenderPipelineAsset : EndNameEditAction
    {
        public override void Action(int instanceId, string pathName, string resourceFile)
        {
            var instance = CreateInstance<TinyRenderPipelineAsset>();
            AssetDatabase.CreateAsset(instance, pathName);
            ResourceReloader.ReloadAllNullIn(instance, packagePath);
            Selection.activeObject = instance;
        }
    }

    [MenuItem("Assets/Create/Rendering/Tiny Render Pipeline Asset")]
    private static void CreateTinyRenderPipeline()
    {
        ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, CreateInstance<CreateTinyRenderPipelineAsset>(), "New RP Asset.asset", null, null);
    }
#endif

    // Renaming rendering layer masks
#if UNITY_EDITOR
    private static string[] m_RenderingLayerNames;
    static TinyRenderPipelineAsset()
    {
        m_RenderingLayerNames = new string[31];
        for (int i = 0; i < m_RenderingLayerNames.Length; ++i)
        {
            m_RenderingLayerNames[i] = "Rendering Layer " + (i + 1);
        }
    }
    public override string[] renderingLayerMaskNames => m_RenderingLayerNames;
    public override string[] prefixedRenderingLayerMaskNames => m_RenderingLayerNames;
#endif

    protected override RenderPipeline CreatePipeline()
    {
        renderPipeline = new TinyRenderPipeline(this);
        return renderPipeline;
    }
}
