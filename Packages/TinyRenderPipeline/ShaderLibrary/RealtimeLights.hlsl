#ifndef TINY_RP_REALTIME_LIGHTS_INCLUDED
#define TINY_RP_REALTIME_LIGHTS_INCLUDED

struct Light
{
    half3 direction;
    half3 color;
    float distanceAttenuation;
    half shadowAttenuation;
};

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

    // Directional lights store direction in lightPosition.xyz and have .w set to 0.0.
    // This way the following code will work for both directional and punctual lights.
    float3 lightVector = lightPositionWS.xyz - positionWS * lightPositionWS.w;
    float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);
    half3 lightDirection = half3(lightVector * rsqrt(distanceSqr)); // normalize direction

    Light light;
    light.direction = lightDirection;
    light.color = color;
    light.distanceAttenuation = 1.0;
    light.shadowAttenuation = 1.0;

    return light;
}

Light GetAdditionalLight(uint i, float3 positionWS)
{
    return GetAdditionalPerObjectLight(i, positionWS);
}

#endif
