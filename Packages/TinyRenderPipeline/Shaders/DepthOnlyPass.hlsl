#ifndef TINY_RP_DEPTH_ONLY_PASS_INCLUDED
#define TINY_RP_DEPTH_ONLY_PASS_INCLUDED

#include "Packages/com.tiny.render-pipeline/ShaderLibrary/Core.hlsl"

struct Attributes
{
    float4 positionOS : POSITION;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
};

Varyings DepthVertex(Attributes input) : SV_POSITION
{
    Varyings output = (Varyings)0;
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    return output;
}

half DepthFragment(Varyings input) : SV_TARGET
{
    return input.positionCS.z;
}

#endif
