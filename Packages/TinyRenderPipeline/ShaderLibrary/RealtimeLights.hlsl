#ifndef TINY_RP_REALTIME_LIGHTS_INCLUDED
#define TINY_RP_REALTIME_LIGHTS_INCLUDED

#include "Packages/com.tiny.render-pipeline/ShaderLibrary/Clustering.hlsl"

#ifdef _FORWARD_PLUS
    #define LIGHT_LOOP_BEGIN(lightCount) { \
    uint lightIndex; \
    ClusterIterator _urp_internal_clusterIterator = ClusterInit(inputData.normalizedScreenSpaceUV, inputData.positionWS, 0); \
    [loop] while (ClusterNext(_urp_internal_clusterIterator, lightIndex)) { \
        lightIndex += URP_FP_DIRECTIONAL_LIGHTS_COUNT;
    #define LIGHT_LOOP_END } }
#else
    #define LIGHT_LOOP_BEGIN(lightCount) \
    for (uint lightIndex = 0u; lightIndex < lightCount; ++lightIndex) {
    #define LIGHT_LOOP_END }
#endif

struct Light
{
    half3  direction;
    half3  color;
    float  distanceAttenuation;
    half   shadowAttenuation;
    uint   layerMask;
};

float DistanceAttenuation(float distanceSqr, float distanceAttenuation)
{
    // For additional directional lights, distanceAttenuation = 0, smoothFactor = 1.0;
    // the final distance attenuation is rcp(distanceSqr), while distanceSqr is a normalized direction vector's length,
    // so for additional directional lights, it will return 1.0.
    float lightAtten = rcp(distanceSqr);

    float factor = distanceSqr * distanceAttenuation;
    float smoothFactor = saturate(1.0 - factor * factor);
    smoothFactor = smoothFactor * smoothFactor;

    return lightAtten * smoothFactor;
}

half AngleAttenuation(half3 spotDirection, half3 lightDirection, half2 spotAttenuation)
{
    half SdotL = dot(spotDirection, lightDirection);
    half atten = saturate(SdotL * spotAttenuation.x + spotAttenuation.y);
    return atten * atten;
}

Light GetMainLight()
{
    Light light;
    light.direction = half3(_MainLightPosition.xyz);
    light.color = _MainLightColor.rgb;
#ifdef _FORWARD_PLUS
    light.distanceAttenuation = 1.0;
#else
    light.distanceAttenuation = unity_LightData.z; // unity_LightData.z is 1 when not culled by the culling mask, otherwise 0.
#endif
    light.shadowAttenuation = 1.0;
    light.layerMask = _MainLightLayerMask;

    return light;
}

Light GetMainLight(InputData inputData)
{
    Light light = GetMainLight();
    light.shadowAttenuation = MainLightShadow(inputData.shadowCoord, inputData.positionWS);
    return light;
}

int GetAdditionalLightsCount()
{
#ifdef _FORWARD_PLUS
    return 0;
#else
    return int(min(_AdditionalLightsCount.x, unity_LightData.y));
#endif
}

int GetPerObjectLightIndex(uint index)
{
    float4 tmp = unity_LightIndices[index / 4];
    return int(tmp[index % 4]);
}

Light GetAdditionalPerObjectLight(int perObjectLightIndex, float3 positionWS)
{
    float4 lightPositionWS = _AdditionalLightsPosition[perObjectLightIndex];
    half3 color = _AdditionalLightsColor[perObjectLightIndex].rgb;
    float4 distanceAndSpotAttenuation = _AdditionalLightsAttenuation[perObjectLightIndex];
    half3 spotDirection = _AdditionalLightsSpotDir[perObjectLightIndex].xyz;
    uint lightLayerMask = asuint(_AdditionalLightsLayerMasks[perObjectLightIndex]);

    // Directional lights store direction in lightPosition.xyz and have .w set to 0.0.
    // This way the following code will work for both directional and punctual lights.
    float3 lightVector = lightPositionWS.xyz - positionWS * lightPositionWS.w;
    float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);
    half3 lightDirection = half3(lightVector * rsqrt(distanceSqr)); // normalize direction

    float attenuation = DistanceAttenuation(distanceSqr, distanceAndSpotAttenuation.x) * AngleAttenuation(spotDirection, lightDirection, distanceAndSpotAttenuation.zw);

    Light light;
    light.direction = lightDirection;
    light.color = color;
    light.distanceAttenuation = attenuation;
    light.shadowAttenuation = 1.0;
    light.layerMask = lightLayerMask;

    return light;
}

Light GetAdditionalLight(uint i, float3 positionWS)
{
#ifdef _FORWARD_PLUS
    int lightIndex = i;
#else
    int lightIndex = GetPerObjectLightIndex(i);
#endif
    Light light = GetAdditionalPerObjectLight(lightIndex, positionWS);
    light.shadowAttenuation = AdditionalLightShadow(lightIndex, positionWS, light.direction);
    return light;
}

#endif
