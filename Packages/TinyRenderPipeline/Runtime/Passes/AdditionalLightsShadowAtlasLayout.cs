using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class AdditionalLightsShadowAtlasLayout
{
    private int m_TotalShadowSlicesCount;
    private int m_ShadowSliceResolution;

    public AdditionalLightsShadowAtlasLayout(ref RenderingData renderingData, int atlasSize)
    {
        ref var cullResults = ref renderingData.cullResults;
        NativeArray<VisibleLight> visibleLights = cullResults.visibleLights;

        int maxVisibleAdditionalLightsCount = TinyRenderPipeline.maxVisibleAdditionalLights;
        int additionalLightsCount = 0;
        // Calculating the maximum shadow slice count (each point light: 6 face shadow slices, each spot light: 1 face shadow slice)
        int totalShadowSlicesCount = 0;
        for (int visibleLightIndex = 0; visibleLightIndex < visibleLights.Length; ++visibleLightIndex)
        {
            if (additionalLightsCount >= maxVisibleAdditionalLightsCount)
                break;

            if (visibleLightIndex == renderingData.mainLightIndex)
                continue;

            if (ShadowUtils.IsValidShadowCastingLight(ref renderingData, visibleLightIndex))
            {
                VisibleLight vl = visibleLights[visibleLightIndex];

                int shadowSlicesCountForThisLight = ShadowUtils.GetAdditionalLightShadowSliceCount(vl.lightType);
                totalShadowSlicesCount += shadowSlicesCountForThisLight;

                ++additionalLightsCount;
            }
        }

        m_TotalShadowSlicesCount = totalShadowSlicesCount;
        m_ShadowSliceResolution = ShadowUtils.GetMaxTileResolutionInAtlas(atlasSize, atlasSize, totalShadowSlicesCount);
    }

    public int GetTotalShadowSlicesCount() => m_TotalShadowSlicesCount;

    public int GetShadowSliceResolution() => m_ShadowSliceResolution;
}
