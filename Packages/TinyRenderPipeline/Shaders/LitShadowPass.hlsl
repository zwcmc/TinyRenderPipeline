#ifndef TINY_RP_LIT_SHADOW_PASS_INCLUDED
#define TINY_RP_LIT_SHADOW_PASS_INCLUDED

// For directional light, xyz: light direction, w: 1.0
// For spot light and point light, xyz: light position, w: 0.0
float4 _LightDirectionOrPosition;

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 texcoord   : TEXCOORD0;
};

struct Varyings
{
#ifdef _ALPHATEST_ON
    float2 uv         : TEXCOORD0;
#endif
    float4 positionCS : SV_POSITION;
};

float4 GetShadowPositionHClip(Attributes input)
{
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
    float3 lightDirection = _LightDirectionOrPosition.w > 0.5 ? _LightDirectionOrPosition.xyz : normalize(_LightDirectionOrPosition.xyz - positionWS);

    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));

#if UNITY_REVERSED_Z
    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif

    return positionCS;
}

Varyings LitShadowVertex(Attributes input)
{
    Varyings output = (Varyings)0;
#ifdef _ALPHATEST_ON
    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
#endif
    output.positionCS = GetShadowPositionHClip(input);
    return output;
}

half4 LitShadowFragment(Varyings input) : SV_TARGET
{
#ifdef _ALPHATEST_ON
    half4 albedoAlpha = SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
    AlphaDiscard(albedoAlpha.a * _BaseColor.a, _Cutoff);
#endif
    return 0;
}

#endif
