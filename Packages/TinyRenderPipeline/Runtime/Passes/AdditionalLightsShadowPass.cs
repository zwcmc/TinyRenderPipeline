using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class AdditionalLightsShadowPass
{
    private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler("AdditionalLightsShadowPass");
    private static readonly ProfilingSampler s_SetAdditionalLightsShadowmapSampler = new ProfilingSampler("SetAdditionalLightsShadowmapGlobal");

    private static readonly Vector4 s_DefaultShadowParams = new Vector4(0, 0, 0, -1);

    private const int k_ShadowmapBufferBits = 16;
    private const string k_AdditionalLightsShadowmapTextureName = "_AdditionalLightsShadowmapTexture";

    // Magic numbers used to identify light type when rendering shadow receiver.
    // Keep in sync with AdditionalLightRealtimeShadow code in Shadows.hlsl
    private const float LightTypeIdentifierInShadowParams_Spot = 0;
    private const float LightTypeIdentifierInShadowParams_Point = 1;

    private int m_AdditionalLightsShadowmapID;
    private RTHandle m_AdditionalLightsShadowmapHandle;
    private RTHandle m_EmptyAdditionalLightsShadowmapHandle;

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

    private bool m_CreateEmptyShadowmap;

    private static class AdditionalShadowsConstantBuffer
    {
        public static int _AdditionalLightsWorldToShadow;
        public static int _AdditionalShadowParams;
        public static int _AdditionalShadowFadeParams;
    }

    private class PassData
    {
        public AdditionalLightsShadowPass pass;

        public TextureHandle shadowmapTexture;
        public RenderingData renderingData;

        public int shadowmapID;
        public bool emptyShadowmap;

        public RendererListHandle[] shadowRendererListHandles;
    }

    public AdditionalLightsShadowPass()
    {
        AdditionalShadowsConstantBuffer._AdditionalLightsWorldToShadow = Shader.PropertyToID("_AdditionalLightsWorldToShadow");
        AdditionalShadowsConstantBuffer._AdditionalShadowParams = Shader.PropertyToID("_AdditionalShadowParams");
        AdditionalShadowsConstantBuffer._AdditionalShadowFadeParams = Shader.PropertyToID("_AdditionalShadowFadeParams");
        m_AdditionalLightsShadowmapID = Shader.PropertyToID(k_AdditionalLightsShadowmapTextureName);

        int maxAdditionalLightsCount = TinyRenderPipeline.maxVisibleAdditionalLights;
        int maxShadowSliceCount = TinyRenderPipeline.maxShadowSlicesCount;
        m_AdditionalLightIndexToShadowParams = new Vector4[maxAdditionalLightsCount];
        m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix = new Matrix4x4[maxShadowSliceCount];
        m_AdditionalLightIndexToVisibleLightIndex = new int[maxAdditionalLightsCount];

        m_EmptyAdditionalLightsShadowmapHandle = ShadowUtils.AllocShadowRT(1, 1, k_ShadowmapBufferBits, name: "_EmptyAdditionalLightShadowmapTexture");
    }

    public bool Setup(ref RenderingData renderingData)
    {
        if (!renderingData.shadowData.additionalLightsShadowEnabled)
        {
            return SetupForEmptyRendering(ref renderingData);
        }

        // Clear data
        Clear();

        m_RenderTargetWidth = renderingData.shadowData.additionalLightsShadowmapWidth;
        m_RenderTargetHeight = renderingData.shadowData.additionalLightsShadowmapHeight;

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
        int shadowmapSplitCount = m_RenderTargetWidth / perShadowSliceResolution;
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

                            m_AdditionalLightsShadowSlices[globalShadowSliceIndex].offsetX = shadowSliceData.offsetX = (globalShadowSliceIndex % shadowmapSplitCount) * perShadowSliceResolution;
                            m_AdditionalLightsShadowSlices[globalShadowSliceIndex].offsetY = shadowSliceData.offsetY = (globalShadowSliceIndex / shadowmapSplitCount) * perShadowSliceResolution;
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

                            m_AdditionalLightsShadowSlices[globalShadowSliceIndex].offsetX = shadowSliceData.offsetX = (globalShadowSliceIndex % shadowmapSplitCount) * perShadowSliceResolution;
                            m_AdditionalLightsShadowSlices[globalShadowSliceIndex].offsetY = shadowSliceData.offsetY = (globalShadowSliceIndex / shadowmapSplitCount) * perShadowSliceResolution;
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

        // Lights that need to be renderer in the shadowmap
        if (validShadowCastingLightsCount == 0)
            return SetupForEmptyRendering(ref renderingData);

        ShadowUtils.ShadowRTReAllocateIfNeeded(ref m_AdditionalLightsShadowmapHandle, m_RenderTargetWidth, m_RenderTargetHeight, k_ShadowmapBufferBits, name: k_AdditionalLightsShadowmapTextureName);

        m_MaxShadowDistanceSq = renderingData.shadowData.maxShadowDistance * renderingData.shadowData.maxShadowDistance;
        m_CascadeBorder = renderingData.shadowData.mainLightShadowCascadeBorder;

        m_CreateEmptyShadowmap = false;

        return true;
    }

    public TextureHandle Record(RenderGraph renderGraph, ref RenderingData renderingData)
    {
        TextureHandle shadowTexture;

        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_ProfilingSampler.name, out var passData, s_ProfilingSampler))
        {
            InitPassData(renderGraph, ref renderingData, ref passData);

            if (!m_CreateEmptyShadowmap)
            {
                for (int globalShadowSliceIndex = 0; globalShadowSliceIndex < m_ShadowSliceToAdditionalLightIndex.Count; ++globalShadowSliceIndex)
                {
                    builder.UseRendererList(passData.shadowRendererListHandles[globalShadowSliceIndex]);
                }

                passData.shadowmapTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, m_AdditionalLightsShadowmapHandle.rt.descriptor, k_AdditionalLightsShadowmapTextureName,
                    true, FilterMode.Bilinear);
                builder.UseTextureFragmentDepth(passData.shadowmapTexture, IBaseRenderGraphBuilder.AccessFlags.Write);
            }

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext rasterGraphContext) =>
            {
                if (!data.emptyShadowmap)
                    data.pass.RenderAdditionalShadowmapAtlas(rasterGraphContext.cmd, ref data, ref data.renderingData);
            });

            shadowTexture = passData.shadowmapTexture;
        }

        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_SetAdditionalLightsShadowmapSampler.name, out var passData, s_SetAdditionalLightsShadowmapSampler))
        {
            passData.pass = this;
            passData.emptyShadowmap = m_CreateEmptyShadowmap;
            passData.shadowmapID = m_AdditionalLightsShadowmapID;
            passData.renderingData = renderingData;
            passData.shadowmapTexture = shadowTexture;

            if (shadowTexture.IsValid())
                builder.UseTexture(shadowTexture, IBaseRenderGraphBuilder.AccessFlags.Read);

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext rasterGraphContext) =>
            {
                if (data.emptyShadowmap)
                {
                    rasterGraphContext.cmd.SetGlobalVectorArray(AdditionalShadowsConstantBuffer._AdditionalShadowParams, m_AdditionalLightIndexToShadowParams);
                    data.shadowmapTexture = rasterGraphContext.defaultResources.defaultShadowTexture;
                }

                rasterGraphContext.cmd.SetGlobalTexture(data.shadowmapID, data.shadowmapTexture);
            });

            return passData.shadowmapTexture;
        }
    }

    public void Dispose()
    {
        m_AdditionalLightsShadowmapHandle?.Release();
        m_EmptyAdditionalLightsShadowmapHandle?.Release();
    }

    private void RenderAdditionalShadowmapAtlas(RasterCommandBuffer cmd, ref PassData data, ref RenderingData renderingData)
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

    private bool SetupForEmptyRendering(ref RenderingData renderingData)
    {
        m_CreateEmptyShadowmap = true;

        ShadowUtils.ShadowRTReAllocateIfNeeded(ref m_EmptyAdditionalLightsShadowmapHandle, 1, 1, k_ShadowmapBufferBits, name: "_EmptyAdditionalLightShadowmapTexture");

        // initialize default _AdditionalShadowParams
        for (int i = 0; i < m_AdditionalLightIndexToShadowParams.Length; ++i)
            m_AdditionalLightIndexToShadowParams[i] = s_DefaultShadowParams;

        for (int i = 0; i < m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix.Length; ++i)
            m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix[i] = Matrix4x4.identity;

        return true;
    }

    private void InitPassData(RenderGraph renderGraph, ref RenderingData renderingData, ref PassData passData)
    {
        passData.pass = this;
        passData.emptyShadowmap = m_CreateEmptyShadowmap;
        passData.shadowmapID = m_AdditionalLightsShadowmapID;
        passData.renderingData = renderingData;

        if (!m_CreateEmptyShadowmap)
        {
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
        }
    }
}
