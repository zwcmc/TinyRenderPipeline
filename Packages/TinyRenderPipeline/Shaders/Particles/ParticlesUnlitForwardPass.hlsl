#ifndef TINY_RP_PARTICLES_UNLIT_FORWARD_PASS_INCLUDED
#define TINY_RP_PARTICLES_UNLIT_FORWARD_PASS_INCLUDED

struct AttributesParticle
{
    float4 positionOS   : POSITION;
    half4 color         : COLOR;
#if defined(_FLIPBOOKBLENDING_ON)
    float4 texcoords    : TEXCOORD0;
    float texcoordBlend : TEXCOORD1;
#else
    float2 texcoords    : TEXCOORD0;
#endif
};

struct VaryingsParticle
{
    float2 uv                 : TEXCOORD0;
#if defined(_FLIPBOOKBLENDING_ON)
    float3 texcoord2AndBlend  : TEXCOORD1;
#endif

#if defined(_FADING_ON)
    float4 projectedPosition : TEXCOORD2;
#endif

    half4 color               : COLOR;
    float4 positionCS         : SV_POSITION;
};

half CameraFade(float near, float far, float4 projection)
{
    float invFadeDistance = rcp(far - near);
    float rawDepth = projection.z / projection.w;
    float thisZ = (unity_OrthoParams.w == 0) ? LinearEyeDepth(rawDepth, _ZBufferParams) : LinearDepthToEyeDepth(rawDepth);
    return half(saturate((thisZ - near) * invFadeDistance));
}

VaryingsParticle ParticleUnlitVertex(AttributesParticle input)
{
    VaryingsParticle output = (VaryingsParticle)0;

#if defined(_FLIPBOOKBLENDING_ON)
    output.uv = input.texcoords.xy;
    output.texcoord2AndBlend.xy = input.texcoords.zw;
    output.texcoord2AndBlend.z = input.texcoordBlend;
#else
    output.uv = input.texcoords.xy;
#endif

    output.color = input.color;

    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

#if defined(_FADING_ON)
    float4 ndc = output.positionCS * 0.5;
    output.projectedPosition.xy = float2(ndc.x, ndc.y * _ProjectionParams.x) + ndc.w;
    output.projectedPosition.zw = output.positionCS.zw;
#endif

    return output;
}

half4 ParticleUnlitFragment(VaryingsParticle input) : SV_Target
{
    half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);

#if defined(_FLIPBOOKBLENDING_ON)
    half4 texColor2 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.texcoord2AndBlend.xy);
    texColor = lerp(texColor, texColor2, half(input.texcoord2AndBlend.z));
#endif

    half3 color = texColor.rgb * _BaseColor.rgb * input.color.rgb;
    half alpha = texColor.a * _BaseColor.a * input.color.a;

    alpha = AlphaDiscard(alpha, _Cutoff);

    alpha = OutputAlpha(alpha, IsSurfaceTypeTransparent(_Surface));

#if defined(_FADING_ON)
    alpha *= CameraFade(_CameraNearFadeDistance, _CameraFarFadeDistance, input.projectedPosition);
#endif

    return half4(color, alpha);
}

#endif
