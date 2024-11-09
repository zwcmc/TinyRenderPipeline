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
    private class CascadeShadowMaps
    {
        [Range(1, 4)]
        public int cascadeCount = 4;

        [Range(0.0f, 1.0f)]
        public float cascadeRatio1 = 0.067f, cascadeRatio2 = 0.2f, cascadeRatio3 = 0.467f;

        [Range(0.0f, 1.0f)]
        public float cascadeBorder = 0.2f;
    }

    // Shadows
    [Serializable]
    private class Shadows
    {
        public float shadowDistance = 50.0f;

        public ShadowResolution mainLightShadowResolution = ShadowResolution._4096;

        public CascadeShadowMaps cascadeShadowMaps;

        public ShadowResolution additionalLightShadowResolution = ShadowResolution._2048;

        public SoftShadows softShadows = SoftShadows.OFF;
    }

    [SerializeField]
    private Shadows m_Shadows = default;

    [SerializeField]
    public PostProcessingData postProcessingData = default;

    [SerializeField]
    public AntialiasingMode antialiasingMode = AntialiasingMode.Off;

    [SerializeField]
    public bool ssaoEnabled = false;

    [Serializable, ReloadGroup]
    public class ShaderResources
    {
        [Reload("Shaders/Antialiasing/FXAA.shader")]
        public Shader fxaaShader;

        [Reload("Shaders/Antialiasing/TAA.shader")]
        public Shader taaShader;

        [Reload("Runtime/Graphics/DepthPyramid/DepthPyramid.compute")]
        public ComputeShader depthPyramidCS;

        [Reload("Runtime/RenderPipeline/Pass/BlitCopy/CopyDepth.compute")]
        public ComputeShader copyDepthCS;

        [Reload("Runtime/RenderPipeline/Pass/BlitCopy/CopyColor.compute")]
        public ComputeShader copyColorCS;
    }

    public float shadowDistance => m_Shadows.shadowDistance;

    public int mainLightShadowMapResolution => (int)m_Shadows.mainLightShadowResolution;

    public int cascadesCount => m_Shadows.cascadeShadowMaps.cascadeCount;

    public Vector3 cascadesSplit
    {
        get
        {
            return new Vector3(
                m_Shadows.cascadeShadowMaps.cascadeRatio1,
                m_Shadows.cascadeShadowMaps.cascadeRatio2,
                m_Shadows.cascadeShadowMaps.cascadeRatio3
            );
        }
    }

    public float cascadeBorder => m_Shadows.cascadeShadowMaps.cascadeBorder;

    public SoftShadows softShadows => m_Shadows.softShadows;

    public int additionalLightsShadowMapResolution => (int)m_Shadows.additionalLightShadowResolution;

    public int colorGradingLutSize = 64;

    public ShaderResources shaderResources;

    public static readonly string packagePath = "Packages/com.zwcmc.tiny-rp";

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
