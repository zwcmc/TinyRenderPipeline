#ifndef TINY_RP_LIT_SHADOW_PASS_INCLUDED
#define TINY_RP_LIT_SHADOW_PASS_INCLUDED

float3 _LightDirection;

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 texcoord   : TEXCOORD0;
};

struct Varyings
{
    float2 uv         : TEXCOORD0;
    float4 positionCS : SV_POSITION;
};

float4 GetShadowPositionHClip(Attributes input)
{
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
    return positionCS;
}

Varyings LitShadowVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    output.positionCS = GetShadowPositionHClip(input);
    return output;
}

half4 LitShadowFragment(Varyings input) : SV_TARGET
{
    half4 albedoAlpha = SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
    AlphaDiscard(albedoAlpha.a * _BaseColor.a, _Cutoff);

    return 0;
}

#endif
