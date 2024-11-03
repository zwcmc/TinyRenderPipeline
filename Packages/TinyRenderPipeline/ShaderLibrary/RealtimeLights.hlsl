#ifndef TINY_RP_REALTIME_LIGHTS_INCLUDED
#define TINY_RP_REALTIME_LIGHTS_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"

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
    half SoL = dot(spotDirection, lightDirection);
    half atten = saturate(SoL * spotAttenuation.x + spotAttenuation.y);
    return atten * atten;
}

Light GetMainLight()
{
    Light light;
    light.direction = half3(_MainLightPosition.xyz);
    light.color = _MainLightColor.rgb;
    light.distanceAttenuation = unity_LightData.z; // unity_LightData.z is 1 when not culled by the culling mask, otherwise 0.
    light.shadowAttenuation = 1.0;
    light.layerMask = _MainLightLayerMask;

    return light;
}

Light GetMainLight(InputData inputData)
{
    Light light = GetMainLight();
    light.shadowAttenuation = MainLightShadow(inputData.shadowCoord, inputData.positionWS, inputData.normalizedScreenSpaceUV);
    return light;
}

int GetAdditionalLightsCount()
{
    return int(min(_AdditionalLightsCount.x, unity_LightData.y));
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
    int lightIndex = GetPerObjectLightIndex(i);
    Light light = GetAdditionalPerObjectLight(lightIndex, positionWS);
    light.shadowAttenuation = AdditionalLightShadow(lightIndex, positionWS, light.direction);
    return light;
}

#endif
