#ifndef TINY_RP_REALTIME_LIGHTS_INCLUDED
#define TINY_RP_REALTIME_LIGHTS_INCLUDED

struct Light
{
    half3 direction;
    half3 color;
};

Light GetMainLight()
{
    Light light;
    light.direction = half3(_MainLightPosition.xyz);
    light.color = _MainLightColor.rgb;
    return light;
}

#endif
