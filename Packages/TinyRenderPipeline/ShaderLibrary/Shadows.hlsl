#ifndef TINY_RP_SHADOWS_INCLUDED
#define TINY_RP_SHADOWS_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Shadow/ShadowSamplingTent.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

#define MAX_SHADOW_CASCADES 4

#define BEYOND_SHADOW_FAR(shadowCoord) shadowCoord.z <= 0.0 || shadowCoord.z >= 1.0

TEXTURE2D_SHADOW(_MainLightShadowMapTexture);
SAMPLER_CMP(sampler_MainLightShadowMapTexture);
TEXTURE2D_SHADOW(_AdditionalLightsShadowMapTexture);
SAMPLER_CMP(sampler_AdditionalLightsShadowMapTexture);

float4 _MainLightShadowMapTexture_TexelSize;

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

// PCSS
float4   _CascadeOffsetScales[MAX_SHADOW_CASCADES + 1];
float4   _DirLightPCSSParams0[MAX_SHADOW_CASCADES + 1];
float4   _DirLightPCSSParams1[MAX_SHADOW_CASCADES + 1];
float4   _DirLightPCSSProjs[MAX_SHADOW_CASCADES + 1];

float4   _AdditionalShadowFadeParams; // x: additional light fade scale, y: additional light fade bias, z: 0.0, w: 0.0)
float4   _AdditionalShadowParams[MAX_VISIBLE_LIGHTS];         // Per-light data
float4x4 _AdditionalLightsWorldToShadow[MAX_SHADOW_SLICE_COUNT];  // Per-shadow-slice-data
#ifndef SHADER_API_GLES3
CBUFFER_END
#endif

#include "PCSS.hlsl"

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
    return float4(shadowCoord.xyz, cascadeIndex);  // xyz: shadow coord, w: cascade index
}

// 1 tap bilinear PCF 2x2
real SampleShadow_PCF_Bilinear(TEXTURE2D_SHADOW_PARAM(shadowMap, sampler_shadowMap), float3 coord)
{
    return SAMPLE_TEXTURE2D_SHADOW(shadowMap, sampler_shadowMap, coord);
}

// 4 tap bilinear PCF 3x3
real SampleShadow_PCF_Bilinear_4Tap_3x3(TEXTURE2D_SHADOW_PARAM(shadowMap, sampler_shadowMap), float3 coord)
{
    float2 offset = _MainLightShadowMapTexture_TexelSize.xy * 0.5;

    real4 result;
    result.x = SampleShadow_PCF_Bilinear(shadowMap, sampler_shadowMap, float3(coord.xy + int2(-1, -1) * offset, coord.z));
    result.y = SampleShadow_PCF_Bilinear(shadowMap, sampler_shadowMap,float3(coord.xy + int2(-1, 1) * offset, coord.z));
    result.z = SampleShadow_PCF_Bilinear(shadowMap, sampler_shadowMap,float3(coord.xy + int2(1, 1) * offset, coord.z));
    result.w = SampleShadow_PCF_Bilinear(shadowMap, sampler_shadowMap,float3(coord.xy + int2(1, -1) * offset, coord.z));
    return real(dot(result, 0.25));
}

// 4 Tap 3x3 Tent Filter PCF
real SampleShadow_PCF_Tent_3x3(TEXTURE2D_SHADOW_PARAM(shadowMap, sampler_shadowMap), float3 coord)
{
    real shadow = 0.0;
    real fetchesWeights[4];
    real2 fetchesUV[4];

    SampleShadow_ComputeSamples_Tent_3x3(_MainLightShadowMapTexture_TexelSize, coord.xy, fetchesWeights, fetchesUV);

    shadow += fetchesWeights[0] * SampleShadow_PCF_Bilinear(shadowMap, sampler_shadowMap, float3(fetchesUV[0].xy, coord.z));
    shadow += fetchesWeights[1] * SampleShadow_PCF_Bilinear(shadowMap, sampler_shadowMap, float3(fetchesUV[1].xy, coord.z));
    shadow += fetchesWeights[2] * SampleShadow_PCF_Bilinear(shadowMap, sampler_shadowMap, float3(fetchesUV[2].xy, coord.z));
    shadow += fetchesWeights[3] * SampleShadow_PCF_Bilinear(shadowMap, sampler_shadowMap, float3(fetchesUV[3].xy, coord.z));

    return shadow;
}

// 9 Tap 5x5 Tent Filter PCF
real SampleShadow_PCF_Tent_5x5(TEXTURE2D_SHADOW_PARAM(shadowMap, sampler_shadowMap), float3 coord)
{
    real shadow = 0.0;
    real fetchesWeights[9];
    real2 fetchesUV[9];

    SampleShadow_ComputeSamples_Tent_5x5(_MainLightShadowMapTexture_TexelSize, coord.xy, fetchesWeights, fetchesUV);

    shadow += fetchesWeights[0] * SampleShadow_PCF_Bilinear(shadowMap, sampler_shadowMap, float3(fetchesUV[0].xy, coord.z));
    shadow += fetchesWeights[1] * SampleShadow_PCF_Bilinear(shadowMap, sampler_shadowMap, float3(fetchesUV[1].xy, coord.z));
    shadow += fetchesWeights[2] * SampleShadow_PCF_Bilinear(shadowMap, sampler_shadowMap, float3(fetchesUV[2].xy, coord.z));
    shadow += fetchesWeights[3] * SampleShadow_PCF_Bilinear(shadowMap, sampler_shadowMap, float3(fetchesUV[3].xy, coord.z));

    shadow += fetchesWeights[4] * SampleShadow_PCF_Bilinear(shadowMap, sampler_shadowMap, float3(fetchesUV[4].xy, coord.z));
    shadow += fetchesWeights[5] * SampleShadow_PCF_Bilinear(shadowMap, sampler_shadowMap, float3(fetchesUV[5].xy, coord.z));
    shadow += fetchesWeights[6] * SampleShadow_PCF_Bilinear(shadowMap, sampler_shadowMap, float3(fetchesUV[6].xy, coord.z));
    shadow += fetchesWeights[7] * SampleShadow_PCF_Bilinear(shadowMap, sampler_shadowMap, float3(fetchesUV[7].xy, coord.z));

    shadow += fetchesWeights[8] * SampleShadow_PCF_Bilinear(shadowMap, sampler_shadowMap, float3(fetchesUV[8].xy, coord.z));

    return shadow;
}

half SampleShadowMap(TEXTURE2D_SHADOW_PARAM(shadowMap, sampler_shadowMap), float4 shadowCoord, half4 shadowParams, float2 positionSS)
{
    real shadowStrength = shadowParams.x;
    real attenuation;

#if defined(_SHADOWS_PCF)
    attenuation = SampleShadow_PCF_Tent_5x5(TEXTURE2D_SHADOW_ARGS(shadowMap, sampler_shadowMap), shadowCoord.xyz);
#elif defined(_SHADOWS_PCSS)
    attenuation = SampleShadow_PCSS(TEXTURE2D_SHADOW_ARGS(shadowMap, sampler_shadowMap), shadowCoord, _MainLightShadowMapTexture_TexelSize, positionSS);
#else
    attenuation = SampleShadow_PCF_Bilinear(shadowMap, sampler_shadowMap, shadowCoord.xyz);
#endif

    attenuation = LerpWhiteTo(attenuation, shadowStrength);

    return BEYOND_SHADOW_FAR(shadowCoord) ? 1.0 : attenuation;
}

half SampleShadowMap_AdditionalLights(TEXTURE2D_SHADOW_PARAM(shadowMap, sampler_shadowMap), float4 shadowCoord, half4 shadowParams)
{
    shadowCoord.xyz /= shadowCoord.w;

    real shadowStrength = shadowParams.x;
    real attenuation = SampleShadow_PCF_Bilinear(shadowMap, sampler_shadowMap, shadowCoord.xyz);

    attenuation = LerpWhiteTo(attenuation, shadowStrength);

    return BEYOND_SHADOW_FAR(shadowCoord) ? 1.0 : attenuation;
}

half MainLightShadow(float4 shadowCoord, float3 positionWS, float2 positionSS)
{
    half realtimeShadow = SampleShadowMap(TEXTURE2D_SHADOW_ARGS(_MainLightShadowMapTexture, sampler_MainLightShadowMapTexture), shadowCoord, _MainLightShadowParams, positionSS);
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

half AdditionalLightShadow(int lightIndex, float3 positionWS, half3 lightDirection)
{
    half4 shadowParams = _AdditionalShadowParams[lightIndex];

    int shadowSliceIndex = shadowParams.w;

    if (shadowSliceIndex < 0)
        return 1.0;

    // 0: spot light, 1: point light
    half isPointLight = shadowParams.z;

    UNITY_BRANCH
    if (isPointLight)
    {
        float cubemapFaceId = CubeMapFaceID(-lightDirection);
        shadowSliceIndex += cubemapFaceId;
    }

    float4 shadowCoord = mul(_AdditionalLightsWorldToShadow[shadowSliceIndex], float4(positionWS, 1.0));
    half realtimeShadow = SampleShadowMap_AdditionalLights(TEXTURE2D_SHADOW_ARGS(_AdditionalLightsShadowMapTexture, sampler_AdditionalLightsShadowMapTexture), shadowCoord, shadowParams);
    half shadowFade = GetAdditionalLightShadowFade(positionWS);
    return lerp(realtimeShadow, 1.0, shadowFade);
}

#endif
