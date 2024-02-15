#ifndef TINY_RP_LIT_FORWARD_PASS_INCLUDED
#define TINY_RP_LIT_FORWARD_PASS_INCLUDED

struct Attributes
{
    float3 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 uv         : TEXCOORD0;
};

struct Varyings
{
    float2 uv         : TEXCOORD0;
    float3 normalWS   : TEXCOORD1;
    float4 positionCS : SV_POSITION;
};

Varyings LitVertex(Attributes input)
{
    Varyings output = (Varyings)0;

    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

    output.normalWS = TransformObjectToWorldNormal(input.normalOS.xyz);

    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    return output;
}

half4 LitFragment(Varyings input) : SV_TARGET
{
    half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
    half3 color = texColor.rgb * _BaseColor.rgb;
    half alpha = texColor.a * _BaseColor.a;

    alpha = AlphaDiscard(alpha, _Cutoff);

    alpha = OutputAlpha(alpha, IsSurfaceTypeTransparent(_Surface));

    return half4(color, alpha);
}

#endif
