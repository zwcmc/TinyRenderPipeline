#ifndef TINY_RP_REALTIME_LIGHTS_INCLUDED
#define TINY_RP_REALTIME_LIGHTS_INCLUDED

struct Light
{
    half3 direction;
    half3 color;
    float distanceAttenuation;
    half shadowAttenuation;
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

Light GetMainLight()
{
    Light light;
    light.direction = half3(_MainLightPosition.xyz);
    light.color = _MainLightColor.rgb;
    light.distanceAttenuation = unity_LightData.z; // unity_LightData.z is 1 when not culled by the culling mask, otherwise 0.
    light.shadowAttenuation = 1.0;
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
    return int(min(_AdditionalLightsCount.x, unity_LightData.y));
}

Light GetAdditionalPerObjectLight(int perObjectLightIndex, float3 positionWS)
{
    float4 lightPositionWS = _AdditionalLightsPosition[perObjectLightIndex];
    half3 color = _AdditionalLightsColor[perObjectLightIndex];
    float4 distanceAndSpotAttenuation = _AdditionalLightsAttenuation[perObjectLightIndex];

    // Directional lights store direction in lightPosition.xyz and have .w set to 0.0.
    // This way the following code will work for both directional and punctual lights.
    float3 lightVector = lightPositionWS.xyz - positionWS * lightPositionWS.w;
    float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);
    half3 lightDirection = half3(lightVector * rsqrt(distanceSqr)); // normalize direction

    float attenuation = DistanceAttenuation(distanceSqr, distanceAndSpotAttenuation.x);

    Light light;
    light.direction = lightDirection;
    light.color = color;
    light.distanceAttenuation = attenuation;
    light.shadowAttenuation = 1.0;

    return light;
}

Light GetAdditionalLight(uint i, float3 positionWS)
{
    return GetAdditionalPerObjectLight(i, positionWS);
}

#endif
