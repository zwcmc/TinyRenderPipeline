#ifndef TINY_RP_SHADOWS_INCLUDED
#define TINY_RP_SHADOWS_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

#define BEYOND_SHADOW_FAR(shadowCoord) shadowCoord.z <= 0.0 || shadowCoord.z >= 1.0

TEXTURE2D_SHADOW(_MainLightShadowmapTexture);
SAMPLER_CMP(sampler_LinearClampCompare);

#ifndef SHADER_API_GLES3
CBUFFER_START(LightShadows)
#endif
float4x4 _MainLightWorldToShadow;
float4 _MainLightShadowParams;  // (x: shadowStrength)
#ifndef SHADER_API_GLES3
CBUFFER_END
#endif

float4 TransformWorldToShadowCoord(float3 positionWS)
{
    float4 shadowCoord = mul(_MainLightWorldToShadow, float4(positionWS, 1.0));
    return float4(shadowCoord.xyz, 0.0);
}

real SampleShadowmap(float4 shadowCoord)
{
    real attenuation = real(SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, shadowCoord.xyz));
    attenuation = LerpWhiteTo(attenuation, _MainLightShadowParams.x);
    return BEYOND_SHADOW_FAR(shadowCoord) ? 1.0 : attenuation;
}

half MainLightRealtimeShadow(float4 shadowCoord)
{
    return SampleShadowmap(shadowCoord);
}

half MainLightShadow(float4 shadowCoord)
{
    return MainLightRealtimeShadow(shadowCoord);
}

#endif
