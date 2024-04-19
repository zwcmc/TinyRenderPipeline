#ifndef TINY_RP_DECLARE_OPAQUE_TEXTURE_INCLUDED
#define TINY_RP_DECLARE_OPAQUE_TEXTURE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

TEXTURE2D(_CameraOpaqueTexture);
SAMPLER(sampler_CameraOpaqueTexture);

float3 SampleSceneColor(float2 uv)
{
    return SAMPLE_TEXTURE2D_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uv, 0.0).rgb;
}

#endif
