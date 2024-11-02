#ifndef TINY_RP_DECLARE_COLOR_TEXTURE_INCLUDED
#define TINY_RP_DECLARE_COLOR_TEXTURE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

TEXTURE2D(_CameraColorTexture);
SAMPLER(sampler_CameraColorTexture);

half3 SampleSceneColor(float2 uv, float lod = 0.0)
{
    return SAMPLE_TEXTURE2D_LOD(_CameraColorTexture, sampler_CameraColorTexture, uv, lod).rgb;
}

#endif
