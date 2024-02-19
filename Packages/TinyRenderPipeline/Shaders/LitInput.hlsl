#ifndef TINY_RP_LIT_INPUT_INCLUDED
#define TINY_RP_LIT_INPUT_INCLUDED

#include "Packages/com.tiny.render-pipeline/ShaderLibrary/Input.hlsl"
#include "Packages/com.tiny.render-pipeline/ShaderLibrary/ShaderVariablesFunctions.hlsl"
#include "Packages/com.tiny.render-pipeline/ShaderLibrary/SurfaceData.hlsl"
#include "Packages/com.tiny.render-pipeline/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.tiny.render-pipeline/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.tiny.render-pipeline/ShaderLibrary/Lighting.hlsl"

CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
half4 _BaseColor;
half _Cutoff;
half _Metallic;
half _Smoothness;
half _Surface;
CBUFFER_END

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

half4 SampleAlbedoAlpha(float2 uv, TEXTURE2D_PARAM(albedoAlphaMap, sampler_albedoAlphaMap))
{
    return half4(SAMPLE_TEXTURE2D(albedoAlphaMap, sampler_albedoAlphaMap, uv));
}

inline void InitializeSurfaceData(float2 uv, out SurfaceData outSurfaceData)
{
    half4 albedoAlpha = SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
    outSurfaceData.alpha = albedoAlpha.a * _BaseColor.a;
    outSurfaceData.alpha = AlphaDiscard(outSurfaceData.alpha, _Cutoff);

    outSurfaceData.albedo = albedoAlpha.rgb * _BaseColor.rgb;

    outSurfaceData.normalTS = half3(0.0h, 0.0h, 1.0h);

    outSurfaceData.metallic = _Metallic;
    outSurfaceData.smoothness = _Smoothness;
}

#endif
