#ifndef TINY_RP_SHADOWS_INCLUDED
#define TINY_RP_SHADOWS_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

#define MAX_SHADOW_CASCADES 4

#define BEYOND_SHADOW_FAR(shadowCoord) shadowCoord.z <= 0.0 || shadowCoord.z >= 1.0

TEXTURE2D_SHADOW(_MainLightShadowmapTexture);
TEXTURE2D_SHADOW(_AdditionalLightsShadowmapTexture);
SAMPLER_CMP(sampler_LinearClampCompare);

#ifndef SHADER_API_GLES3
CBUFFER_START(LightShadows)
#endif
float4x4 _MainLightWorldToShadow[MAX_SHADOW_CASCADES + 1];
float4   _MainLightShadowParams;  // (x: shadowStrength, y: 0.0, z: main light last cascade fade scale, w: main light last cascade fade bias)
// four cascades' culling spheres, each data: xyz: the sphere center position, w: square of the sphere's radius
float4   _CascadeShadowSplitSpheres0;
float4   _CascadeShadowSplitSpheres1;
float4   _CascadeShadowSplitSpheres2;
float4   _CascadeShadowSplitSpheres3;
float4   _CascadesParams; // (x: cascades count, y: 0.0, z: 0.0, w: 0.0)

float4   _AdditionalShadowFadeParams; // x: additional light fade scale, y: additional light fade bias, z: 0.0, w: 0.0)
float4   _AdditionalShadowParams[MAX_VISIBLE_LIGHTS];         // Per-light data
float4x4 _AdditionalLightsWorldToShadow[MAX_VISIBLE_LIGHTS];  // Per-shadow-slice-data
#ifndef SHADER_API_GLES3
CBUFFER_END
#endif

float4 _ShadowBias; // x: depth bias, y: normal bias

float3 ApplyShadowBias(float3 positionWS, float3 normalWS, float3 lightDirection)
{
    float invNdotL = 1.0 - saturate(dot(lightDirection, normalWS));
    float scale = invNdotL * _ShadowBias.y;

    positionWS = lightDirection * _ShadowBias.xxx + positionWS;
    positionWS = normalWS * scale.xxx + positionWS;
    return positionWS;
}

half GetMainLightShadowFade(float3 positionWS)
{
    float3 camToPixel = positionWS - _WorldSpaceCameraPos;
    float distanceCamToPixelSq = dot(camToPixel, camToPixel);

    return half(saturate(distanceCamToPixelSq * _MainLightShadowParams.z + _MainLightShadowParams.w));
}

half ComputeCascadeIndex(float3 positionWS)
{
    float3 fromCenter0 = positionWS - _CascadeShadowSplitSpheres0.xyz;
    float3 fromCenter1 = positionWS - _CascadeShadowSplitSpheres1.xyz;
    float3 fromCenter2 = positionWS - _CascadeShadowSplitSpheres2.xyz;
    float3 fromCenter3 = positionWS - _CascadeShadowSplitSpheres3.xyz;
    // Square of radius from each culling sphere's center
    float4 distance2 = float4(dot(fromCenter0, fromCenter0), dot(fromCenter1, fromCenter1), dot(fromCenter2, fromCenter2), dot(fromCenter3, fromCenter3));
    // Check in which culling sphere, e.g. : (1,0,0,0) means in culling sphere 0.
    half4 weights = half4(distance2 < float4(_CascadeShadowSplitSpheres0.w, _CascadeShadowSplitSpheres1.w, _CascadeShadowSplitSpheres2.w, _CascadeShadowSplitSpheres3.w));
    weights.yzw = saturate(weights.yzw - weights.xyz);

    return half(4.0) - dot(weights, half4(4, 3, 2, 1));
}

float4 TransformWorldToShadowCoord(float3 positionWS)
{
    half cascadeIndex = _CascadesParams.x > 1.0 ? ComputeCascadeIndex(positionWS) : 0.0;
    float4 shadowCoord = mul(_MainLightWorldToShadow[cascadeIndex], float4(positionWS, 1.0));
    return float4(shadowCoord.xyz, 0.0);
}

real SampleShadowmap(TEXTURE2D_SHADOW_PARAM(shadowMap, sampler_shadowMap), float4 shadowCoord, half4 shadowParams, bool isPerspectiveProjection = true)
{
    if (isPerspectiveProjection)
        shadowCoord.xyz /= shadowCoord.w;

    real shadowStrength = shadowParams.x;
    real attenuation = real(SAMPLE_TEXTURE2D_SHADOW(shadowMap, sampler_shadowMap, shadowCoord.xyz));
    attenuation = LerpWhiteTo(attenuation, shadowStrength);
    return BEYOND_SHADOW_FAR(shadowCoord) ? 1.0 : attenuation;
}

half MainLightShadow(float4 shadowCoord, float3 positionWS)
{
    half realtimeShadow = SampleShadowmap(TEXTURE2D_ARGS(_MainLightShadowmapTexture, sampler_LinearClampCompare), shadowCoord, _MainLightShadowParams, false);
    half shadowFade = GetMainLightShadowFade(positionWS);
    return lerp(realtimeShadow, 1.0, shadowFade);
}

half GetAdditionalLightShadowFade(float3 positionWS)
{
    float3 camToPixel = positionWS - _WorldSpaceCameraPos;
    float distanceCamToPixel2 = dot(camToPixel, camToPixel);
    float fade = saturate(distanceCamToPixel2 * float(_AdditionalShadowFadeParams.x) + float(_AdditionalShadowFadeParams.y));
    return half(fade);
}

half AdditionalLightShadow(int lightIndex, float3 positionWS)
{
    half4 shadowParams = _AdditionalShadowParams[lightIndex];

    int shadowSliceIndex = shadowParams.w;
    if (shadowSliceIndex < 0)
        return 1.0;

    float4 shadowCoord = mul(_AdditionalLightsWorldToShadow[shadowSliceIndex], float4(positionWS, 1.0));
    half realtimeShadow = SampleShadowmap(TEXTURE2D_ARGS(_AdditionalLightsShadowmapTexture, sampler_LinearClampCompare), shadowCoord, shadowParams, true);
    half shadowFade = GetAdditionalLightShadowFade(positionWS);
    return lerp(realtimeShadow, 1.0, shadowFade);
}

#endif
