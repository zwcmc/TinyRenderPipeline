#ifndef TINY_RP_LIT_INPUT_INCLUDED
#define TINY_RP_LIT_INPUT_INCLUDED

#include "Packages/com.zwcmc.tiny-rp/ShaderLibrary/Core.hlsl"

CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
half4 _BaseColor;
half4 _EmissionColor;
half _Smoothness;
half _Metallic;
half _BumpScale;
half _Surface;
CBUFFER_END

TEXTURE2D(_BaseMap);                         SAMPLER(sampler_BaseMap);
TEXTURE2D(_MetallicGlossMap);                SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_BumpMap);                         SAMPLER(sampler_BumpMap);
TEXTURE2D(_EmissionMap);                     SAMPLER(sampler_EmissionMap);
TEXTURE2D(_IBL_DFG);                         SAMPLER(sampler_IBL_DFG);

#include "Packages/com.zwcmc.tiny-rp/ShaderLibrary/SurfaceData.hlsl"
#include "Packages/com.zwcmc.tiny-rp/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.zwcmc.tiny-rp/ShaderLibrary/ImageBasedLighting.hlsl"
#include "Packages/com.zwcmc.tiny-rp/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.zwcmc.tiny-rp/ShaderLibrary/Lighting.hlsl"

half4 SampleAlbedoAlpha(float2 uv, TEXTURE2D_PARAM(albedoAlphaMap, sampler_albedoAlphaMap))
{
    return half4(SAMPLE_TEXTURE2D(albedoAlphaMap, sampler_albedoAlphaMap, uv));
}

half4 SampleMetallicGlossMap(float2 uv)
{
    half4 gloss;
#ifdef _METALLICGLOSSMAP
    gloss = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv);
    gloss.a *= _Smoothness;
#else
    gloss.rgb = _Metallic.rrr;
    gloss.a = _Smoothness;
#endif

    return gloss;
}

half3 SampleNormal(float2 uv, TEXTURE2D_PARAM(bumpMap, sampler_bumpMap), half scale = half(1.0))
{
#ifdef _NORMALMAP
    half4 n = SAMPLE_TEXTURE2D(bumpMap, sampler_bumpMap, uv);
    return UnpackNormalScale(n, scale);
#else
    return half3(0.0h, 0.0h, 1.0h);
#endif
}

half3 SampleEmission(float2 uv, half3 emissionColor, TEXTURE2D_PARAM(emissionMap, sampler_emissionMap))
{
#ifndef _EMISSION
    return 0.0h;
#else
    return SAMPLE_TEXTURE2D(emissionMap, sampler_emissionMap, uv).rgb * emissionColor;
#endif
}

void InitializeSurfaceData(float2 uv, out SurfaceData outSurfaceData)
{
    half4 albedoAlpha = SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
    outSurfaceData.alpha = albedoAlpha.a * _BaseColor.a;

    outSurfaceData.baseColor = albedoAlpha.rgb * _BaseColor.rgb;

    outSurfaceData.normalTS = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
    outSurfaceData.emission = SampleEmission(uv, _EmissionColor.rgb, TEXTURE2D_ARGS(_EmissionMap, sampler_EmissionMap));

    half4 gloss = SampleMetallicGlossMap(uv);
    outSurfaceData.metallic = gloss.r;
    outSurfaceData.smoothness = gloss.a;
}

#endif
