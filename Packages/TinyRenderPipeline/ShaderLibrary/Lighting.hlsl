#ifndef TINY_RP_LIGHTING_INCLUDED
#define TINY_RP_LIGHTING_INCLUDED

#include "Packages/com.tiny.render-pipeline/ShaderLibrary/RealtimeLights.hlsl"
#include "Packages/com.tiny.render-pipeline/ShaderLibrary/GlobalIllumination.hlsl"

half3 LightingPBR(BRDFData brdfData, Light light, half3 normalWS, half3 viewDirectionWS)
{
    half3 lightDirectionWS = light.direction;
    half NdotL = saturate(dot(normalWS, lightDirectionWS));
    float lightAttenuation = light.distanceAttenuation * light.shadowAttenuation;
    half3 radiance = light.color * (lightAttenuation * NdotL);

    half3 brdf = brdfData.diffuse;
    brdf += brdfData.specular * DirectBRDFSpecular(brdfData, normalWS, lightDirectionWS, viewDirectionWS);

    return brdf * radiance;
}

half4 FragmentPBR(InputData inputData, SurfaceData surfaceData)
{
    BRDFData brdfData;
    InitializeBRDFData(surfaceData, brdfData);

    uint meshRenderingLayers = GetMeshRenderingLayer();

    half3 lightingColor = 0.0;
    Light mainLight = GetMainLight(inputData);

    half3 emissionColor = surfaceData.emission;
    lightingColor += emissionColor;

    half3 giColor = GlobalIllumination(brdfData, inputData.bakedGI, surfaceData.occlusion, inputData.normalWS, inputData.viewDirectionWS);
    lightingColor += giColor;

    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
    {
        half3 mainLightColor = LightingPBR(brdfData, mainLight, inputData.normalWS, inputData.viewDirectionWS);
        lightingColor += mainLightColor;
    }

    half3 additionalLightsColor = 0.0;
    uint additionalLightCount = GetAdditionalLightsCount();

    // If using forward+ rendering path, the additional directional lighting is calculated separately, because directional lighting affects every objects, and does not calculated in Clustering
#if _FORWARD_PLUS
    for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        Light light = GetAdditionalLight(lightIndex, inputData.positionWS);
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
        {
            additionalLightsColor += LightingPBR(brdfData, light, inputData.normalWS, inputData.viewDirectionWS);
        }
    }
#endif

    LIGHT_LOOP_BEGIN(additionalLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData.positionWS);
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
        {
            additionalLightsColor += LightingPBR(brdfData, light, inputData.normalWS, inputData.viewDirectionWS);
        }
    LIGHT_LOOP_END

    lightingColor += additionalLightsColor;

#if defined(REAL_IS_HALF)
    return min(half4(lightingColor, surfaceData.alpha), HALF_MAX);
#else
    return half4(lightingColor, surfaceData.alpha);
#endif
}

#endif
