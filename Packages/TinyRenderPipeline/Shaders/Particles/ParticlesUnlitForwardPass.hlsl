#ifndef TINY_RP_PARTICLES_UNLIT_FORWARD_PASS_INCLUDED
#define TINY_RP_PARTICLES_UNLIT_FORWARD_PASS_INCLUDED

#include "Packages/com.tiny.render-pipeline/ShaderLibrary//DeclareDepthTexture.hlsl"
#include "Packages/com.tiny.render-pipeline/ShaderLibrary//DeclareOpaqueTexture.hlsl"

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

#if defined(_FADING_ON) || defined(_SOFTPARTICLES_ON) || defined(_DISTORTION_ON)
    float4 projectedPosition  : TEXCOORD2;
#endif

#if defined(_SOFTPARTICLES_ON)
    float3 positionWS         : TEXCOORD3;
#endif

    half4 color               : COLOR;
    float4 positionCS         : SV_POSITION;
};

half4 BlendTexture(TEXTURE2D_PARAM(_Texture, sampler_Texture), float2 uv, float3 blendUV)
{
    half4 color = SAMPLE_TEXTURE2D(_Texture, sampler_Texture, uv);
#if defined(_FLIPBOOKBLENDING_ON)
    half4 color2 = SAMPLE_TEXTURE2D(_Texture, sampler_Texture, blendUV.xy);
    color = lerp(color, color2, blendUV.z);
#endif
    return color;
}

float3 SampleNormalTS(TEXTURE2D_PARAM(_BumpMap, sampler_BumpMap), float2 uv, float3 blendUV)
{
#if defined(_DISTORTION_NORMALMAP)
    float4 normalTS = BlendTexture(TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), uv, blendUV);
    return UnpackNormal(normalTS);
#else
    return float3(0.0, 0.0, 1.0);
#endif
}

half CameraFade(float near, float far, float4 projection)
{
    float invFadeDistance = rcp(far - near);
    float rawDepth = projection.z / projection.w;
    float thisZ = (unity_OrthoParams.w == 0) ? LinearEyeDepth(rawDepth, _ZBufferParams) : LinearDepthToEyeDepth(rawDepth);
    return half(saturate((thisZ - near) * invFadeDistance));
}

half SoftParticles(float near, float far, float4 projection, float3 positionWS)
{
    float fade = 1.0;

    if (near > 0.0 || far > 0.0)
    {
        float rawDepth = SampleSceneDepth(projection.xy / projection.w);
        float sceneZ = (unity_OrthoParams.w == 0) ? LinearEyeDepth(rawDepth, _ZBufferParams) : LinearDepthToEyeDepth(rawDepth);
        float thisZ = LinearEyeDepth(positionWS.xyz, GetWorldToViewMatrix());
        float invFadeDistance = rcp(far - near);
        fade = saturate(invFadeDistance * ((sceneZ - near) - thisZ));
    }

    return half(fade);
}

half3 Distortion(half3 albedo, half alpha, float3 normal, float4 projection)
{
    float strengthScale = 0.1;
    float2 screenUV = projection.xy / projection.w + normal.xy * (_DistortionStrength * strengthScale) * alpha;
    half3 distortion = half3(SampleSceneColor(screenUV));
    return lerp(distortion, albedo, saturate(alpha - _DistortionBlend));
}

void InitBlendUV(VaryingsParticle input, out float3 blendUV)
{
#if defined(_FLIPBOOKBLENDING_ON)
    blendUV = input.texcoord2AndBlend;
#else
    blendUV = float3(0,0,0);
#endif
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

#if defined(_FADING_ON) || defined(_SOFTPARTICLES_ON) || defined(_DISTORTION_ON)
    float4 ndc = output.positionCS * 0.5;
    output.projectedPosition.xy = float2(ndc.x, ndc.y * _ProjectionParams.x) + ndc.w;
    output.projectedPosition.zw = output.positionCS.zw;
#endif

#if defined(_SOFTPARTICLES_ON)
    output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
#endif

    return output;
}

half4 ParticleUnlitFragment(VaryingsParticle input) : SV_Target
{
    float3 blendUV;
    InitBlendUV(input, blendUV);

    half4 texColor = BlendTexture(TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap), input.uv, blendUV);

    half3 color = texColor.rgb * _BaseColor.rgb * input.color.rgb;
    half alpha = texColor.a * _BaseColor.a * input.color.a;

    alpha = AlphaDiscard(alpha, _Cutoff);

    alpha = OutputAlpha(alpha, IsSurfaceTypeTransparent(_Surface));

#if defined(_SOFTPARTICLES_ON)
    alpha *= SoftParticles(_SoftParticlesNearFadeDistance, _SoftParticlesFarFadeDistance, input.projectedPosition, input.positionWS);
#endif

#if defined(_FADING_ON)
    alpha *= CameraFade(_CameraNearFadeDistance, _CameraFarFadeDistance, input.projectedPosition);
#endif

#if defined(_DISTORTION_ON)
    float3 normalTS = SampleNormalTS(TEXTURE2D_ARGS(_DistortionNormal, sampler_DistortionNormal), input.uv, blendUV);
    color = Distortion(color, alpha, normalTS, input.projectedPosition);
#endif

    return half4(color, alpha);
}

#endif
