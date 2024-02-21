#ifndef TINY_RP_SHADOWS_INCLUDED
#define TINY_RP_SHADOWS_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

#define MAX_SHADOW_CASCADES 4

#define BEYOND_SHADOW_FAR(shadowCoord) shadowCoord.z <= 0.0 || shadowCoord.z >= 1.0

TEXTURE2D_SHADOW(_MainLightShadowmapTexture);
SAMPLER_CMP(sampler_LinearClampCompare);

#ifndef SHADER_API_GLES3
CBUFFER_START(LightShadows)
#endif
float4x4 _MainLightWorldToShadow[MAX_SHADOW_CASCADES + 1];
float4   _MainLightShadowParams;  // (x: shadowStrength, y: 0.0, z: 0.0, w: 0.0)
// four cascades' culling spheres, each data: xyz: the sphere center position, w: the sphere's radius
float4   _CascadeShadowSplitSpheres0;
float4   _CascadeShadowSplitSpheres1;
float4   _CascadeShadowSplitSpheres2;
float4   _CascadeShadowSplitSpheres3;
// (x: square of culling sphere0's radius, y: square of culling sphere1's radius, z: square of culling sphere2's radius, w: square of culling sphere3's radius)
float4   _CascadeShadowSplitSphereRadii;
#ifndef SHADER_API_GLES3
CBUFFER_END
#endif

half ComputeCascadeIndex(float3 positionWS)
{
    float3 fromCenter0 = positionWS - _CascadeShadowSplitSpheres0.xyz;
    float3 fromCenter1 = positionWS - _CascadeShadowSplitSpheres1.xyz;
    float3 fromCenter2 = positionWS - _CascadeShadowSplitSpheres2.xyz;
    float3 fromCenter3 = positionWS - _CascadeShadowSplitSpheres3.xyz;
    // Square of radius from each culling sphere's center
    float4 distance2 = float4(dot(fromCenter0, fromCenter0), dot(fromCenter1, fromCenter1), dot(fromCenter2, fromCenter2), dot(fromCenter3, fromCenter3));
    // Check in which culling sphere, e.g. : (1,0,0,0) means in culling sphere 0.
    half4 weights = half4(distance2 < _CascadeShadowSplitSphereRadii);
    weights.yzw = saturate(weights.yzw - weights.xyz);

    return half(4.0) - dot(weights, half4(4, 3, 2, 1));
}

float4 TransformWorldToShadowCoord(float3 positionWS)
{
    half cascadeIndex = ComputeCascadeIndex(positionWS);
    float4 shadowCoord = mul(_MainLightWorldToShadow[cascadeIndex], float4(positionWS, 1.0));
    return float4(shadowCoord.xyz, 0.0);
}

real SampleShadowmap(float4 shadowCoord)
{
    real attenuation = real(SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, shadowCoord.xyz));
    attenuation = LerpWhiteTo(attenuation, _MainLightShadowParams.x);
    return BEYOND_SHADOW_FAR(shadowCoord) ? 1.0 : attenuation;
}

half MainLightShadow(float4 shadowCoord)
{
    half realtimeShadow = SampleShadowmap(shadowCoord);
    return realtimeShadow;
}

#endif
