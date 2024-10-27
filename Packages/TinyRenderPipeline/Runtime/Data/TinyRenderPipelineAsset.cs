#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
#endif
using System;
using UnityEngine;
using UnityEngine.Rendering;

public class TinyRenderPipelineAsset : RenderPipelineAsset<TinyRenderPipeline>
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
    private Shadows m_Shadows = default;

    [SerializeField]
    private PostProcessingData m_PostProcessingData = default;

    [SerializeField]
    [Range(32, 64)]
    private int m_ColorGradingLutSize = 32;

    [SerializeField]
    [Range(0.1f, 2f)]
    private float m_RenderScale = 1f;

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

    public PostProcessingData postProcessingData
    {
        get { return m_PostProcessingData; }
    }

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
        var renderPipeline = new TinyRenderPipeline(this);
        return renderPipeline;
    }
}
