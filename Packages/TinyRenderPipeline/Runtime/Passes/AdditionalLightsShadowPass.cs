using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class AdditionalLightsShadowPass
{
    private int m_AdditionalLightsShadowmapID;
    private RTHandle m_AdditionalLightsShadowmapHandle;
    private RTHandle m_EmptyAdditionalLightsShadowmapHandle;

    private const int k_ShadowmapBufferBits = 16;
    private const string k_AdditionalLightsShadowmapTextureName = "_AdditionalLightsShadowmapTexture";
    private static readonly Vector4 c_DefaultShadowParams = new Vector4(0, 0, 0, -1);

    // Magic numbers used to identify light type when rendering shadow receiver.
    // Keep in sync with AdditionalLightRealtimeShadow code in Shadows.hlsl
    private const float LightTypeIdentifierInShadowParams_Spot = 0;
    private const float LightTypeIdentifierInShadowParams_Point = 1;

    private float m_CascadeBorder;
    private float m_MaxShadowDistanceSq;

    private int m_RenderTargetWidth;
    private int m_RenderTargetHeight;

    private List<int> m_ShadowSliceToAdditionalLightIndex = new List<int>();
    int[] m_AdditionalLightIndexToVisibleLightIndex = null;

    private ShadowSliceData[] m_AdditionalLightsShadowSlices = null;
    private Vector4[] m_AdditionalLightIndexToShadowParams = null;  // per-additional-light shadow info (x: shadowStrength, y: 0.0, z: light type, w: perLightFirstShadowSliceIndex)
    private Matrix4x4[] m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix = null; // per-shadow-slice shadow transform matrix

    private static readonly ProfilingSampler m_ProfilingSampler = new ProfilingSampler("AdditionalLightsShadow");

    private static class AdditionalShadowsConstantBuffer
    {
        public static int _AdditionalLightsWorldToShadow;
        public static int _AdditionalShadowParams;
        public static int _AdditionalShadowFadeParams;
    }

    public AdditionalLightsShadowPass()
    {
        AdditionalShadowsConstantBuffer._AdditionalLightsWorldToShadow = Shader.PropertyToID("_AdditionalLightsWorldToShadow");
        AdditionalShadowsConstantBuffer._AdditionalShadowParams = Shader.PropertyToID("_AdditionalShadowParams");
        AdditionalShadowsConstantBuffer._AdditionalShadowFadeParams = Shader.PropertyToID("_AdditionalShadowFadeParams");
        m_AdditionalLightsShadowmapID = Shader.PropertyToID(k_AdditionalLightsShadowmapTextureName);

        int maxAdditionalLightsCount = TinyRenderPipeline.maxVisibleAdditionalLights;
        m_AdditionalLightIndexToShadowParams = new Vector4[maxAdditionalLightsCount];
        m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix = new Matrix4x4[maxAdditionalLightsCount];
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
        ref AdditionalLightsShadowAtlasLayout atlasLayout = ref renderingData.shadowData.additionalLightsShadowAtlasLayout;

        int maxAdditionalLightShadowParams = Math.Min(visibleLights.Length, TinyRenderPipeline.maxVisibleAdditionalLights);

        if (m_AdditionalLightIndexToVisibleLightIndex.Length < maxAdditionalLightShadowParams)
        {
            m_AdditionalLightIndexToVisibleLightIndex = new int[maxAdditionalLightShadowParams];
            m_AdditionalLightIndexToShadowParams = new Vector4[maxAdditionalLightShadowParams];
        }

        int totalShadowSliceCount = atlasLayout.GetTotalShadowSlicesCount();
        int perShadowSliceResolution = atlasLayout.GetShadowSliceResolution();
        int shadowmapSplitCount = m_RenderTargetWidth / perShadowSliceResolution;

        float fovBias = ShadowUtils.GetPointLightShadowFrustumFovBiasInDegrees(perShadowSliceResolution);

        if (m_AdditionalLightsShadowSlices == null || m_AdditionalLightsShadowSlices.Length < totalShadowSliceCount)
            m_AdditionalLightsShadowSlices = new ShadowSliceData[totalShadowSliceCount];

        if (m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix == null || m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix.Length < totalShadowSliceCount)
            m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix = new Matrix4x4[totalShadowSliceCount];

        // Initialize shadow params
        for (int i = 0; i < maxAdditionalLightShadowParams; ++i)
        {
            m_AdditionalLightIndexToShadowParams[i] = c_DefaultShadowParams;
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

            int additionalLightIndex = additionalLightCount++;
            if (additionalLightIndex >= m_AdditionalLightIndexToVisibleLightIndex.Length)
                continue;

            m_AdditionalLightIndexToVisibleLightIndex[additionalLightIndex] = visibleLightIndex;

            if (m_ShadowSliceToAdditionalLightIndex.Count >= totalShadowSliceCount || additionalLightIndex >= maxAdditionalLightShadowParams)
                continue;

            LightType lightType = vl.lightType;
            int perLightShadowSlicesCount = ShadowUtils.GetAdditionalLightShadowSliceCount(lightType);
            if (m_ShadowSliceToAdditionalLightIndex.Count + perLightShadowSlicesCount > totalShadowSliceCount && ShadowUtils.IsValidShadowCastingLight(ref cullResults, renderingData.mainLightIndex, visibleLightIndex))
                break;

            int perLightFirstShadowSliceIndex = m_ShadowSliceToAdditionalLightIndex.Count;

            bool isValidShadowCastingLight = false;

            for (int perLightShadowSlice = 0; perLightShadowSlice < perLightShadowSlicesCount; ++perLightShadowSlice)
            {
                int globalShadowSliceIndex = m_ShadowSliceToAdditionalLightIndex.Count;
                bool lightRangeContainsShadowCasters = cullResults.GetShadowCasterBounds(visibleLightIndex, out var shadowCasterBounds);
                if (lightRangeContainsShadowCasters)
                {
                    if (!renderingData.shadowData.additionalLightsShadowEnabled)
                        continue;

                    if (ShadowUtils.IsValidShadowCastingLight(ref cullResults, renderingData.mainLightIndex, visibleLightIndex))
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
                            bool success = ShadowUtils.ExtractPointLightMatrix(ref cullResults, visibleLightIndex, (CubemapFace)perLightShadowSlice, fovBias, out ShadowSliceData shadowSliceData);
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
            }

            if (isValidShadowCastingLight)
                validShadowCastingLightsCount++;
        }

        // Lights that need to be renderer in the shadowmap
        if (validShadowCastingLightsCount == 0)
            return SetupForEmptyRendering(ref renderingData);

        ShadowUtils.ShadowRTReAllocateIfNeeded(ref m_AdditionalLightsShadowmapHandle, m_RenderTargetWidth, m_RenderTargetHeight, k_ShadowmapBufferBits, name: "_AdditionalLightsShadowmapTexture");

        m_MaxShadowDistanceSq = renderingData.shadowData.maxShadowDistance * renderingData.shadowData.maxShadowDistance;
        m_CascadeBorder = renderingData.shadowData.mainLightShadowCascadeBorder;

        return true;
    }

    public void Render(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cmd = renderingData.commandBuffer;

        // Setup render target and clear target
        cmd.SetRenderTarget(m_AdditionalLightsShadowmapHandle, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
        cmd.ClearRenderTarget(true, false, Color.clear);
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        using (new ProfilingScope(cmd, m_ProfilingSampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            ref var cullResults = ref renderingData.cullResults;
            NativeArray<VisibleLight> visibleLights = cullResults.visibleLights;
            int shadowSlicesCount = m_ShadowSliceToAdditionalLightIndex.Count;

            for (int globalShadowSliceIndex = 0; globalShadowSliceIndex < shadowSlicesCount; ++globalShadowSliceIndex)
            {
                int additionalLightIndex = m_ShadowSliceToAdditionalLightIndex[globalShadowSliceIndex];

                // we do the shadow strength check here again here because we might have zero strength for non-shadow-casting lights.
                // In that case we need the shadow data buffer but we can skip rendering them to shadowmap.
                if (Mathf.Approximately(m_AdditionalLightIndexToShadowParams[additionalLightIndex].x, 0.0f) || Mathf.Approximately(m_AdditionalLightIndexToShadowParams[additionalLightIndex].w, -1.0f))
                    continue;

                int shadowLightIndex = m_AdditionalLightIndexToVisibleLightIndex[additionalLightIndex];
                VisibleLight shadowLight = visibleLights[shadowLightIndex];
                ShadowSliceData shadowSliceData = m_AdditionalLightsShadowSlices[globalShadowSliceIndex];

                var shadowDrawingSettings = new ShadowDrawingSettings(cullResults, shadowLightIndex);
                shadowDrawingSettings.splitData = shadowSliceData.splitData;

                Vector4 shadowBias = ShadowUtils.GetShadowBias(shadowLight, shadowLightIndex, shadowSliceData.projectionMatrix, shadowSliceData.resolution);
                ShadowUtils.SetupShadowCasterConstantBuffer(cmd, shadowLight, shadowBias);

                cmd.SetViewport(new Rect(shadowSliceData.offsetX, shadowSliceData.offsetY, shadowSliceData.resolution, shadowSliceData.resolution));
                cmd.SetViewProjectionMatrices(shadowSliceData.viewMatrix, shadowSliceData.projectionMatrix);

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                context.DrawShadows(ref shadowDrawingSettings);
            }

            cmd.SetGlobalTexture(m_AdditionalLightsShadowmapID, m_AdditionalLightsShadowmapHandle.nameID);
            cmd.SetGlobalVectorArray(AdditionalShadowsConstantBuffer._AdditionalShadowParams, m_AdditionalLightIndexToShadowParams);                         // per-light data
            cmd.SetGlobalMatrixArray(AdditionalShadowsConstantBuffer._AdditionalLightsWorldToShadow, m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix); // per-shadow-slice data

            ShadowUtils.GetScaleAndBiasForLinearDistanceFade(m_MaxShadowDistanceSq, m_CascadeBorder, out float shadowFadeScale, out float shadowFadeBias);
            cmd.SetGlobalVector(AdditionalShadowsConstantBuffer._AdditionalShadowFadeParams, new Vector4(shadowFadeScale, shadowFadeBias, 0, 0));

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
    }

    public void Dispose()
    {
        m_AdditionalLightsShadowmapHandle?.Release();
        m_EmptyAdditionalLightsShadowmapHandle?.Release();
    }

    private void Clear()
    {
        m_ShadowSliceToAdditionalLightIndex.Clear();
    }

    private bool SetupForEmptyRendering(ref RenderingData renderingData)
    {
        var cmd = renderingData.commandBuffer;
        var context = renderingData.renderContext;

        ShadowUtils.ShadowRTReAllocateIfNeeded(ref m_EmptyAdditionalLightsShadowmapHandle, 1, 1, k_ShadowmapBufferBits, name: "_EmptyAdditionalLightShadowmapTexture");
        cmd.SetGlobalTexture(m_AdditionalLightsShadowmapID, m_EmptyAdditionalLightsShadowmapHandle.nameID);

        // initialize _AdditionalShadowParams
        for (int i = 0; i < m_AdditionalLightIndexToShadowParams.Length; ++i)
            m_AdditionalLightIndexToShadowParams[i] = c_DefaultShadowParams;

        cmd.SetGlobalVectorArray(AdditionalShadowsConstantBuffer._AdditionalShadowParams, m_AdditionalLightIndexToShadowParams);

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        return false;
    }
}
