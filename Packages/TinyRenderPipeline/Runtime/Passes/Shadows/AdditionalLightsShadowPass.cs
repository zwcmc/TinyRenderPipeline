using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class AdditionalLightsShadowPass
{
    private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler("AdditionalLights ShadowMap");
    private static readonly ProfilingSampler s_SetEmptyAdditionalLightsShadowMapSampler = new ProfilingSampler("Set Empty AdditionalLights ShadowMap");

    private static readonly Vector4 s_DefaultShadowParams = new Vector4(0, 0, 0, -1);

    private const int k_ShadowMapBufferBits = 16;
    private const string k_AdditionalLightsShadowMapTextureName = "_AdditionalLightsShadowMapTexture";

    // Magic numbers used to identify light type when rendering shadow receiver.
    // Keep in sync with AdditionalLightRealtimeShadow code in Shadows.hlsl
    private const float LightTypeIdentifierInShadowParams_Spot = 0;
    private const float LightTypeIdentifierInShadowParams_Point = 1;

    // private int m_AdditionalLightsShadowMapID;
    private RTHandle m_AdditionalLightsShadowMapHandle;

    private float m_CascadeBorder;
    private float m_MaxShadowDistanceSq;

    private int m_RenderTargetWidth;
    private int m_RenderTargetHeight;

    // For each shadow slice, store the "additional light indices" of the punctual light that casts it
    // For example, there are one main light and two punctual lights in the scene;
    // the first punctual light is a spot light and its additional light index is 0 (the additional light index starts from 0),
    // the second punctual light is a point light and its additional light index is 1,
    // so m_ShadowSliceToAdditionalLightIndex stores [0, 1, 1, 1, 1, 1, 1], where a spot light has one shadow slice and a point light has six shadow slices.
    private List<int> m_ShadowSliceToAdditionalLightIndex = new List<int>();

    // Maps additional light index (index to m_AdditionalLightIndexToShadowParams, starts from 0) to its "global" visible light index (index to renderingData.lightData.visibleLights)
    private int[] m_AdditionalLightIndexToVisibleLightIndex = null;

    private ShadowSliceData[] m_AdditionalLightsShadowSlices = null;  // per-shadow-slice data (view matrix, projection matrix, shadow split data, ...)
    private Vector4[] m_AdditionalLightIndexToShadowParams = null;  // per-additional-light shadow info (x: shadowStrength, y: 0.0, z: light type, w: perLightFirstShadowSliceIndex)
    private Matrix4x4[] m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix = null; // per-shadow-slice shadow transform matrix

    private static class AdditionalShadowsConstantBuffer
    {
        public static int _AdditionalLightsWorldToShadow = Shader.PropertyToID("_AdditionalLightsWorldToShadow");
        public static int _AdditionalShadowParams = Shader.PropertyToID("_AdditionalShadowParams");
        public static int _AdditionalShadowFadeParams = Shader.PropertyToID("_AdditionalShadowFadeParams");
        public static int AdditionalLightsShadowMapID = Shader.PropertyToID(k_AdditionalLightsShadowMapTextureName);
    }

    private class PassData
    {
        public AdditionalLightsShadowPass pass;

        public TextureHandle shadowMapTexture;
        public RenderingData renderingData;

        public int shadowMapID;

        public Vector4[] shadowParams;
        public Matrix4x4[] shadowMatrices;

        public RendererListHandle[] shadowRendererListHandles;
    }

    public AdditionalLightsShadowPass()
    {
        int maxAdditionalLightsCount = TinyRenderPipeline.maxVisibleAdditionalLights;
        int maxShadowSliceCount = TinyRenderPipeline.maxShadowSlicesCount;
        m_AdditionalLightIndexToShadowParams = new Vector4[maxAdditionalLightsCount];
        m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix = new Matrix4x4[maxShadowSliceCount];
        m_AdditionalLightIndexToVisibleLightIndex = new int[maxAdditionalLightsCount];
    }

    private bool SetEmptyAdditionalLightsShadowMap(RenderGraph renderGraph)
    {
        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_SetEmptyAdditionalLightsShadowMapSampler.name, out var passData, s_SetEmptyAdditionalLightsShadowMapSampler))
        {
            // Initialize default _AdditionalShadowParams
            for (int i = 0; i < m_AdditionalLightIndexToShadowParams.Length; ++i)
                m_AdditionalLightIndexToShadowParams[i] = s_DefaultShadowParams;

            for (int i = 0; i < m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix.Length; ++i)
                m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix[i] = Matrix4x4.identity;

            passData.shadowMapTexture = renderGraph.defaultResources.defaultShadowTexture;
            passData.shadowMapID = AdditionalShadowsConstantBuffer.AdditionalLightsShadowMapID;
            passData.shadowParams = m_AdditionalLightIndexToShadowParams;
            passData.shadowMatrices = m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix;

            // Not allow pass culling
            builder.AllowPassCulling(false);
            // Enable this to set global state
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext rasterGraphContext) =>
            {
                var cmd = rasterGraphContext.cmd;

                cmd.SetGlobalTexture(data.shadowMapID, data.shadowMapTexture);
                cmd.SetGlobalVectorArray(AdditionalShadowsConstantBuffer._AdditionalShadowParams, data.shadowParams);
                cmd.SetGlobalMatrixArray(AdditionalShadowsConstantBuffer._AdditionalLightsWorldToShadow, data.shadowMatrices);
            });
        }

        return false;
    }

    public bool IsEnabled(RenderGraph renderGraph, ref RenderingData renderingData)
    {
        if (!renderingData.shadowData.additionalLightsShadowEnabled)
        {
            return SetEmptyAdditionalLightsShadowMap(renderGraph);
        }

        // Clear data
        Clear();

        m_RenderTargetWidth = renderingData.shadowData.additionalLightsShadowMapWidth;
        m_RenderTargetHeight = renderingData.shadowData.additionalLightsShadowMapHeight;

        ref var cullResults = ref renderingData.cullResults;
        var visibleLights = cullResults.visibleLights;
        AdditionalLightsShadowAtlasLayout atlasLayout = new AdditionalLightsShadowAtlasLayout(ref renderingData, m_RenderTargetWidth);

        int maxAdditionalLightShadowParams = Math.Min(visibleLights.Length, TinyRenderPipeline.maxVisibleAdditionalLights);

        if (m_AdditionalLightIndexToVisibleLightIndex.Length < maxAdditionalLightShadowParams)
        {
            m_AdditionalLightIndexToVisibleLightIndex = new int[maxAdditionalLightShadowParams];
            m_AdditionalLightIndexToShadowParams = new Vector4[maxAdditionalLightShadowParams];
        }

        int totalShadowSliceCount = atlasLayout.GetTotalShadowSlicesCount();
        int perShadowSliceResolution = atlasLayout.GetShadowSliceResolution();
        int shadowMapSplitCount = m_RenderTargetWidth / perShadowSliceResolution;
        float pointLightFovBias = ShadowUtils.GetPointLightShadowFrustumFovBiasInDegrees(perShadowSliceResolution);

        if (m_AdditionalLightsShadowSlices == null || m_AdditionalLightsShadowSlices.Length < totalShadowSliceCount)
            m_AdditionalLightsShadowSlices = new ShadowSliceData[totalShadowSliceCount];

        if (m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix == null)
            m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix = new Matrix4x4[TinyRenderPipeline.maxShadowSlicesCount];

        // Initialize shadow params
        for (int i = 0; i < maxAdditionalLightShadowParams; ++i)
        {
            m_AdditionalLightIndexToShadowParams[i] = s_DefaultShadowParams;
        }

        int validShadowCastingLightsCount = 0;
        int additionalLightCount = 0;
        for (int visibleLightIndex = 0; visibleLightIndex < visibleLights.Length; ++visibleLightIndex)
        {
            VisibleLight vl = visibleLights[visibleLightIndex];

            // Skip main light
            if (visibleLightIndex == renderingData.mainLightIndex)
            {
                continue;
            }

            // Check the light affects any shadow casting objects in scene
            if (!cullResults.GetShadowCasterBounds(visibleLightIndex, out var shadowCasterBounds))
            {
                continue;
            }

            int additionalLightIndex = additionalLightCount++;
            if (additionalLightIndex >= m_AdditionalLightIndexToVisibleLightIndex.Length)
                continue;

            // map additional light index to visible light index
            m_AdditionalLightIndexToVisibleLightIndex[additionalLightIndex] = visibleLightIndex;

            if (m_ShadowSliceToAdditionalLightIndex.Count >= totalShadowSliceCount || additionalLightIndex >= maxAdditionalLightShadowParams)
            {
                continue;
            }

            LightType lightType = vl.lightType;
            int perLightShadowSlicesCount = ShadowUtils.GetAdditionalLightShadowSliceCount(lightType);
            if ((m_ShadowSliceToAdditionalLightIndex.Count + perLightShadowSlicesCount > totalShadowSliceCount) && ShadowUtils.IsValidShadowCastingLight(ref renderingData, visibleLightIndex))
            {
                break;
            }

            int perLightFirstShadowSliceIndex = m_ShadowSliceToAdditionalLightIndex.Count;

            bool isValidShadowCastingLight = false;
            for (int perLightShadowSlice = 0; perLightShadowSlice < perLightShadowSlicesCount; ++perLightShadowSlice)
            {
                int globalShadowSliceIndex = m_ShadowSliceToAdditionalLightIndex.Count;
                if (ShadowUtils.IsValidShadowCastingLight(ref renderingData, visibleLightIndex))
                {
                    if (lightType == LightType.Spot)
                    {
                        bool success = ShadowUtils.ExtractSpotLightMatrix(ref cullResults, visibleLightIndex, out ShadowSliceData shadowSliceData);
                        if (success)
                        {
                            m_ShadowSliceToAdditionalLightIndex.Add(additionalLightIndex);

                            m_AdditionalLightsShadowSlices[globalShadowSliceIndex].viewMatrix = shadowSliceData.viewMatrix;
                            m_AdditionalLightsShadowSlices[globalShadowSliceIndex].projectionMatrix = shadowSliceData.projectionMatrix;
                            m_AdditionalLightsShadowSlices[globalShadowSliceIndex].splitData = shadowSliceData.splitData;

                            m_AdditionalLightsShadowSlices[globalShadowSliceIndex].offsetX = shadowSliceData.offsetX = (globalShadowSliceIndex % shadowMapSplitCount) * perShadowSliceResolution;
                            m_AdditionalLightsShadowSlices[globalShadowSliceIndex].offsetY = shadowSliceData.offsetY = (globalShadowSliceIndex / shadowMapSplitCount) * perShadowSliceResolution;
                            m_AdditionalLightsShadowSlices[globalShadowSliceIndex].resolution = shadowSliceData.resolution = perShadowSliceResolution;
                            ShadowUtils.ApplySliceTransform(ref shadowSliceData, m_RenderTargetWidth, m_RenderTargetHeight);

                            var light = vl.light;
                            m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix[globalShadowSliceIndex] = shadowSliceData.shadowTransform;

                            Vector4 shadowParams = new Vector4(light.shadowStrength, 0, LightTypeIdentifierInShadowParams_Spot, perLightFirstShadowSliceIndex);
                            m_AdditionalLightIndexToShadowParams[additionalLightIndex] = shadowParams;
                            isValidShadowCastingLight = true;
                        }
                    }
                    else if (lightType == LightType.Point)
                    {
                        bool success = ShadowUtils.ExtractPointLightMatrix(ref cullResults, visibleLightIndex, (CubemapFace)perLightShadowSlice, pointLightFovBias, out ShadowSliceData shadowSliceData);
                        if (success)
                        {
                            m_ShadowSliceToAdditionalLightIndex.Add(additionalLightIndex);

                            m_AdditionalLightsShadowSlices[globalShadowSliceIndex].viewMatrix = shadowSliceData.viewMatrix;
                            m_AdditionalLightsShadowSlices[globalShadowSliceIndex].projectionMatrix = shadowSliceData.projectionMatrix;
                            m_AdditionalLightsShadowSlices[globalShadowSliceIndex].splitData = shadowSliceData.splitData;

                            m_AdditionalLightsShadowSlices[globalShadowSliceIndex].offsetX = shadowSliceData.offsetX = (globalShadowSliceIndex % shadowMapSplitCount) * perShadowSliceResolution;
                            m_AdditionalLightsShadowSlices[globalShadowSliceIndex].offsetY = shadowSliceData.offsetY = (globalShadowSliceIndex / shadowMapSplitCount) * perShadowSliceResolution;
                            m_AdditionalLightsShadowSlices[globalShadowSliceIndex].resolution = shadowSliceData.resolution = perShadowSliceResolution;
                            ShadowUtils.ApplySliceTransform(ref shadowSliceData, m_RenderTargetWidth, m_RenderTargetHeight);

                            var light = vl.light;
                            m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix[globalShadowSliceIndex] = shadowSliceData.shadowTransform;

                            Vector4 shadowParams = new Vector4(light.shadowStrength, 0, LightTypeIdentifierInShadowParams_Point, perLightFirstShadowSliceIndex);
                            m_AdditionalLightIndexToShadowParams[additionalLightIndex] = shadowParams;

                            isValidShadowCastingLight = true;
                        }
                    }
                }
            }

            if (isValidShadowCastingLight)
                validShadowCastingLightsCount++;
        }

        // Lights that need to be renderer in the shadowMap
        if (validShadowCastingLightsCount == 0)
            return SetEmptyAdditionalLightsShadowMap(renderGraph);

        ShadowUtils.ShadowRTReAllocateIfNeeded(ref m_AdditionalLightsShadowMapHandle, m_RenderTargetWidth, m_RenderTargetHeight, k_ShadowMapBufferBits, name: k_AdditionalLightsShadowMapTextureName);

        m_MaxShadowDistanceSq = renderingData.shadowData.maxShadowDistance * renderingData.shadowData.maxShadowDistance;
        m_CascadeBorder = renderingData.shadowData.mainLightShadowCascadeBorder;

        return true;
    }

    public TextureHandle RecordRenderGraph(RenderGraph renderGraph, ref RenderingData renderingData)
    {
        TextureHandle shadowTexture;
        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_ProfilingSampler.name, out var passData, s_ProfilingSampler))
        {
            passData.pass = this;
            passData.shadowMapID = AdditionalShadowsConstantBuffer.AdditionalLightsShadowMapID;
            passData.renderingData = renderingData;

            var cullResults = renderingData.cullResults;

            passData.shadowRendererListHandles = new RendererListHandle[m_ShadowSliceToAdditionalLightIndex.Count];

            for (int globalShadowSliceIndex = 0; globalShadowSliceIndex < m_ShadowSliceToAdditionalLightIndex.Count; ++globalShadowSliceIndex)
            {
                int additionalLightIndex = m_ShadowSliceToAdditionalLightIndex[globalShadowSliceIndex];
                int visibleLightIndex = m_AdditionalLightIndexToVisibleLightIndex[additionalLightIndex];
                var settings = new ShadowDrawingSettings(cullResults, visibleLightIndex);
                settings.useRenderingLayerMaskTest = true;

                passData.shadowRendererListHandles[globalShadowSliceIndex] = renderGraph.CreateShadowRendererList(ref settings);
            }

            for (int globalShadowSliceIndex = 0; globalShadowSliceIndex < m_ShadowSliceToAdditionalLightIndex.Count; ++globalShadowSliceIndex)
            {
                builder.UseRendererList(passData.shadowRendererListHandles[globalShadowSliceIndex]);
            }

            shadowTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, m_AdditionalLightsShadowMapHandle.rt.descriptor, k_AdditionalLightsShadowMapTextureName, false, FilterMode.Bilinear);
            builder.UseTextureFragmentDepth(shadowTexture, IBaseRenderGraphBuilder.AccessFlags.Write);

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext rasterGraphContext) =>
            {
                data.pass.RenderAdditionalShadowMapAtlas(rasterGraphContext.cmd, ref data, ref data.renderingData);
            });
        }

        RenderingUtils.SetGlobalRenderGraphTextureID(renderGraph, AdditionalShadowsConstantBuffer.AdditionalLightsShadowMapID, shadowTexture, "Set AdditionalLights ShadowMap");

        return shadowTexture;
    }

    public void Dispose()
    {
        m_AdditionalLightsShadowMapHandle?.Release();
    }

    private void RenderAdditionalShadowMapAtlas(RasterCommandBuffer cmd, ref PassData data, ref RenderingData renderingData)
    {
        ref var cullResults = ref renderingData.cullResults;
        NativeArray<VisibleLight> visibleLights = cullResults.visibleLights;
        int shadowSlicesCount = m_ShadowSliceToAdditionalLightIndex.Count;
        bool anyShadowSliceRenderer = false;

        for (int globalShadowSliceIndex = 0; globalShadowSliceIndex < shadowSlicesCount; ++globalShadowSliceIndex)
        {
            int additionalLightIndex = m_ShadowSliceToAdditionalLightIndex[globalShadowSliceIndex];

            if (Mathf.Approximately(m_AdditionalLightIndexToShadowParams[additionalLightIndex].x, 0.0f) || Mathf.Approximately(m_AdditionalLightIndexToShadowParams[additionalLightIndex].w, -1.0f))
                continue;

            int shadowLightIndex = m_AdditionalLightIndexToVisibleLightIndex[additionalLightIndex];
            VisibleLight shadowLight = visibleLights[shadowLightIndex];
            ShadowSliceData shadowSliceData = m_AdditionalLightsShadowSlices[globalShadowSliceIndex];

            Vector4 shadowBias = ShadowUtils.GetShadowBias(shadowLight, shadowLightIndex, shadowSliceData.projectionMatrix, shadowSliceData.resolution);
            ShadowUtils.SetupShadowCasterConstantBuffer(cmd, shadowLight, shadowBias);

            RendererList rendererList = data.shadowRendererListHandles[globalShadowSliceIndex];
            cmd.SetViewport(new Rect(shadowSliceData.offsetX, shadowSliceData.offsetY, shadowSliceData.resolution, shadowSliceData.resolution));
            cmd.SetViewProjectionMatrices(shadowSliceData.viewMatrix, shadowSliceData.projectionMatrix);
            if(rendererList.isValid)
                cmd.DrawRendererList(rendererList);

            anyShadowSliceRenderer = true;
        }

        if (anyShadowSliceRenderer)
        {
            cmd.SetGlobalVectorArray(AdditionalShadowsConstantBuffer._AdditionalShadowParams, m_AdditionalLightIndexToShadowParams);                         // per-additional-light data
            cmd.SetGlobalMatrixArray(AdditionalShadowsConstantBuffer._AdditionalLightsWorldToShadow, m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix); // per-shadow-slice data

            ShadowUtils.GetScaleAndBiasForLinearDistanceFade(m_MaxShadowDistanceSq, m_CascadeBorder, out float shadowFadeScale, out float shadowFadeBias);
            cmd.SetGlobalVector(AdditionalShadowsConstantBuffer._AdditionalShadowFadeParams, new Vector4(shadowFadeScale, shadowFadeBias, 0, 0));
        }
    }

    private void Clear()
    {
        m_ShadowSliceToAdditionalLightIndex.Clear();

        for (int i = 0; i < m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix.Length; ++i)
            m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix[i] = Matrix4x4.identity;
    }
}
