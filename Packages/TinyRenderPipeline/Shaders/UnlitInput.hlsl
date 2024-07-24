#ifndef TINY_RP_UNLIT_INPUT_INCLUDED
#define TINY_RP_UNLIT_INPUT_INCLUDED

#include "Packages/com.tiny.render-pipeline/ShaderLibrary/Core.hlsl"

CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
half4 _BaseColor;
half _Cutoff;
half _Surface;
CBUFFER_END

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

#endif
