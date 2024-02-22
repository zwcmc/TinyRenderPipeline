#ifndef TINY_RP_REALTIME_LIGHTS_INCLUDED
#define TINY_RP_REALTIME_LIGHTS_INCLUDED

struct Light
{
    half3 direction;
    half3 color;
    half shadowAttenuation;
};

Light GetMainLight()
{
    Light light;
    light.direction = half3(_MainLightPosition.xyz);
    light.color = _MainLightColor.rgb;
    light.shadowAttenuation = 1.0;
    return light;
}

Light GetMainLight(InputData inputData)
{
    Light light = GetMainLight();
    light.shadowAttenuation = MainLightShadow(inputData.shadowCoord, inputData.positionWS);
    return light;
}

#endif
