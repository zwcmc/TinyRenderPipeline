using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class TinyRenderPipeline : RenderPipeline
{
    private readonly TinyRenderPipelineAsset pipelineAsset;

    private TinyRenderer m_TinyRenderer;

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

    public TinyRenderPipeline(TinyRenderPipelineAsset asset)
    {
        pipelineAsset = asset;

        // Enable SRP batcher
        GraphicsSettings.useScriptableRenderPipelineBatching = asset.useSRPBatcher;
        // Light intensity in linear space
        GraphicsSettings.lightsUseLinearIntensity = true;

        m_TinyRenderer = new TinyRenderer();
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
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        m_TinyRenderer?.Dispose(disposing);
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

            // Render UI in Scene view.
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
        // Set max shadow distance to use for the cull
        cullingParameters.shadowDistance = Mathf.Min(pipelineAsset.shadowDistance, camera.farClipPlane);
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
        renderingData.cullResults = cullResults;
        renderingData.mainLightIndex = GetMainLightIndex(cullResults.visibleLights);

        renderingData.mainLightShadowmapWidth = asset.mainLightShadowmapResolution;
        renderingData.mainLightShadowmapHeight = asset.mainLightShadowmapResolution;
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
