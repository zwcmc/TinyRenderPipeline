#ifndef TINY_RP_DECLARE_DEPTH_TEXTURE_INCLUDED
#define TINY_RP_DECLARE_DEPTH_TEXTURE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

TEXTURE2D_FLOAT(_CameraDepthTexture);
SAMPLER(sampler_CameraDepthTexture);

float4 _CameraDepthTexture_TexelSize;

float SampleSceneDepth(float2 uv)
{
    return SAMPLE_TEXTURE2D_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, uv, 0.0).r;
}

#endif
