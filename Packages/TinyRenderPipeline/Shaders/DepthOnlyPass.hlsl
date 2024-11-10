#ifndef TINY_RP_DEPTH_ONLY_PASS_INCLUDED
#define TINY_RP_DEPTH_ONLY_PASS_INCLUDED

#include "Packages/com.zwcmc.tiny-rp/ShaderLibrary/Core.hlsl"

struct Attributes
{
    float4 positionOS : POSITION;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
};

Varyings DepthVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    return output;
}

float DepthFragment(Varyings input) : SV_Target0
{
    return input.positionCS.z;
}

#endif
