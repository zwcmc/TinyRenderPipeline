using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class TinyRenderPipeline : RenderPipeline
{
    // This limit matches same limit in Input.hlsl
    private const int k_MaxVisibleAdditionalLights = 8;

    private readonly TinyRenderPipelineAsset pipelineAsset;
    private TinyRenderer m_TinyRenderer;

    public static RTHandleResourcePool s_RTHandlePool;

    private static class Profiling
    {
        private static Dictionary<int, ProfilingSampler> s_HashSamplerCache = new Dictionary<int, ProfilingSampler>();
        public static ProfilingSampler TryGetOrAddCameraSampler(Camera camera)
        {
            ProfilingSampler ps = null;
            int cameraId = camera.GetHashCode();
            bool exists = s_HashSamplerCache.TryGetValue(cameraId, out ps);
            if (!exists)
            {
                ps = new ProfilingSampler($"{nameof(TinyRenderPipeline)}: {camera.name}");
                s_HashSamplerCache.Add(cameraId, ps);
            }

            return ps;
        }
    }

    public static int maxVisibleAdditionalLights => k_MaxVisibleAdditionalLights;

    // public static TinyRenderPipelineAsset asset
    // {
    //     get => GraphicsSettings.currentRenderPipeline as TinyRenderPipelineAsset;
    // }

    public TinyRenderPipeline(TinyRenderPipelineAsset asset)
    {
        pipelineAsset = asset;

        // Enable SRP batcher
        GraphicsSettings.useScriptableRenderPipelineBatching = asset.useSRPBatcher;
        // Light intensity in linear space
        GraphicsSettings.lightsUseLinearIntensity = true;

        m_TinyRenderer = new TinyRenderer(pipelineAsset);

        s_RTHandlePool = new RTHandleResourcePool();
    }

    protected override void Render(ScriptableRenderContext context, Camera[] cameras)
    {
        Render(context, new List<Camera>(cameras));
    }

    protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
    {
        for (int i = 0; i < cameras.Count; ++i)
        {
            var camera = cameras[i];
            RenderSingleCamera(context, camera);
        }

        s_RTHandlePool.PurgeUnusedResources(Time.frameCount);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        m_TinyRenderer?.Dispose(disposing);

        s_RTHandlePool.Cleanup();
        s_RTHandlePool = null;
    }

    private void RenderSingleCamera(ScriptableRenderContext context, Camera camera)
    {
        if (m_TinyRenderer == null)
            return;

        if (!TryGetCullingParameters(camera, out var cullingParameters))
            return;

        CommandBuffer cmd = CommandBufferPool.Get();
        ProfilingSampler sampler = Profiling.TryGetOrAddCameraSampler(camera);
        using (new ProfilingScope(cmd, sampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            // Setup culling parameters
            SetupCullingParameters(ref cullingParameters, ref camera);


            // Emit scene/game view UI. The main game camera UI is always rendered, so this needs to be handled only for different camera types
            if (camera.cameraType == CameraType.Reflection || camera.cameraType == CameraType.Preview)
                ScriptableRenderContext.EmitGeometryForCamera(camera);
#if UNITY_EDITOR
            if (camera.cameraType == CameraType.SceneView)
                ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
#endif

            // Culling
            var cullResults = context.Cull(ref cullingParameters);

            // Initialize rendering data
            InitializeRenderingData(pipelineAsset, ref cullResults, context, cmd, camera, out var renderingData);

            // Rendering
            m_TinyRenderer.Execute(ref renderingData);
        }

        // Execute command buffer
        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);

        // Submit
        context.Submit();
    }

    private void SetupCullingParameters(ref ScriptableCullingParameters cullingParameters, ref Camera camera)
    {
        // Set maximum visible lights: maximum additional lights and one main light
        cullingParameters.maximumVisibleLights = maxVisibleAdditionalLights + 1;

        // Disable shadow casters if the shadow distance turn down to zero
        bool isShadowDistanceZero = Mathf.Approximately(pipelineAsset.shadowDistance, 0.0f);
        if (isShadowDistanceZero)
        {
            cullingParameters.cullingOptions &= ~CullingOptions.ShadowCasters;
        }

        // Set maximum shadow distance to use for the cull
        cullingParameters.shadowDistance = Mathf.Min(pipelineAsset.shadowDistance, camera.farClipPlane);

        // Use conservative method for calculating culling sphere
        cullingParameters.conservativeEnclosingSphere = true;
        // Default number of iterations
        cullingParameters.numIterationsEnclosingSphere = 64;
    }

    private static bool TryGetCullingParameters(Camera camera, out ScriptableCullingParameters cullingParameters)
    {
        return camera.TryGetCullingParameters(out cullingParameters);
    }

    private static void InitializeRenderingData(TinyRenderPipelineAsset asset, ref CullingResults cullResults, ScriptableRenderContext context,
        CommandBuffer cmd, Camera camera, out RenderingData renderingData)
    {
        renderingData.renderContext = context;
        renderingData.commandBuffer = cmd;
        renderingData.camera = camera;
        renderingData.isHdrEnabled = camera.allowHDR && asset.supportsHDR;
        renderingData.cameraTargetDescriptor = RenderingUtils.CreateRenderTextureDescriptor(renderingData.camera, renderingData.isHdrEnabled);

        var cameraRect = camera.rect;
        renderingData.isDefaultCameraViewport = !(Math.Abs(cameraRect.x) > 0.0f || Math.Abs(cameraRect.y) > 0.0f || Math.Abs(cameraRect.width) < 1.0f || Math.Abs(cameraRect.height) < 1.0f);
        renderingData.cullResults = cullResults;

        var visibleLights = cullResults.visibleLights;
        int mainLightIndex = GetMainLightIndex(cullResults.visibleLights);

        bool mainLightCastShadows = false;
        bool additionalLightsCastShadows = false;
        if (asset.shadowDistance > 0.0f)
        {
            mainLightCastShadows = mainLightIndex != -1 &&
                                   visibleLights[mainLightIndex].light != null &&
                                   visibleLights[mainLightIndex].light.shadows != LightShadows.None;

            for (int i = 0; i < visibleLights.Length; ++i)
            {
                if (i == mainLightIndex)
                    continue;

                VisibleLight vl = visibleLights[i];
                Light light = vl.light;
                if ((vl.lightType == LightType.Point || vl.lightType == LightType.Spot) && light != null && light.shadows != LightShadows.None)
                {
                    additionalLightsCastShadows = true;
                    break;
                }
            }
        }

        renderingData.mainLightIndex = mainLightIndex;
        renderingData.additionalLightsCount = Math.Min((renderingData.mainLightIndex != -1) ? visibleLights.Length - 1 : visibleLights.Length, maxVisibleAdditionalLights);

        // Shadow data
        renderingData.shadowData.mainLightShadowsEnabled = SystemInfo.supportsShadows && mainLightCastShadows;
        renderingData.shadowData.cascadesCount = asset.cascadesCount;
        renderingData.shadowData.cascadesSplit = asset.cascadesSplit;
        renderingData.shadowData.mainLightShadowmapWidth = renderingData.shadowData.mainLightShadowmapHeight = asset.mainLightShadowmapResolution;

        renderingData.shadowData.maxShadowDistance = asset.shadowDistance;
        renderingData.shadowData.mainLightShadowCascadeBorder = asset.cascadeBorder;

        renderingData.shadowData.additionalLightsShadowEnabled = SystemInfo.supportsShadows && additionalLightsCastShadows;
        renderingData.shadowData.additionalLightsShadowmapWidth = renderingData.shadowData.additionalLightsShadowmapHeight = asset.additionalLightsShadowmapResolution;

        renderingData.perObjectData = GetPerObjectLightFlags(renderingData.additionalLightsCount);

        renderingData.postProcessingData = asset.postProcessingData;

        renderingData.lutSize = asset.colorGradingLutSize;

        renderingData.copyDepthTexture = asset.requireDepthTexture;
        renderingData.copyColorTexture = asset.requireColorTexture;
    }

    private static PerObjectData GetPerObjectLightFlags(int additionalLightsCount)
    {
        var configuration = PerObjectData.LightProbe | PerObjectData.ReflectionProbes | PerObjectData.LightData;
        if (additionalLightsCount > 0)
            configuration |= PerObjectData.LightIndices;
        return configuration;
    }

    private static int GetMainLightIndex(NativeArray<VisibleLight> visibleLights)
    {
        int totalVisibleLights = visibleLights.Length;

        if (totalVisibleLights == 0)
            return -1;

        Light sunLight = RenderSettings.sun;
        int brightestDirectionalLightIndex = -1;
        float brightestLightIntensity = 0.0f;
        for (int i = 0; i < totalVisibleLights; ++i)
        {
            VisibleLight currVisibleLight = visibleLights[i];
            Light currLight = currVisibleLight.light;

            if (currLight == null)
                break;

            if (currVisibleLight.lightType == LightType.Directional)
            {
                if (currLight == sunLight)
                    return i;

                if (currLight.intensity > brightestLightIntensity)
                {
                    brightestLightIntensity = currLight.intensity;
                    brightestDirectionalLightIndex = i;
                }
            }
        }

        return brightestDirectionalLightIndex;
    }
}
