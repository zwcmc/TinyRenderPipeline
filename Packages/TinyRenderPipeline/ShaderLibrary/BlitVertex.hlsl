#ifndef TINY_RP_BLIT_VERTEX_INCLUDED
#define TINY_RP_BLIT_VERTEX_INCLUDED

#include "Packages/com.zwcmc.tiny-rp/ShaderLibrary/Input.hlsl"

// Blit source texture
TEXTURE2D(_BlitTexture);
SAMPLER(sampler_BlitTexture);
float4 _BlitTexture_TexelSize;

float4 _BlitScaleBias;
#define DYNAMIC_SCALING_APPLY_SCALEBIAS(uv) uv * _BlitScaleBias.xy + _BlitScaleBias.zw

struct Attributes
{
    uint vertexID : SV_VertexID;
};

struct Varyings
{
    float2 texcoord : TEXCOORD0;
    float4 positionCS : SV_POSITION;
};

Varyings Vert(Attributes input)
{
    Varyings output = (Varyings)0;

    float4 pos = GetFullScreenTriangleVertexPosition(input.vertexID);
    float2 uv  = GetFullScreenTriangleTexCoord(input.vertexID);

    output.positionCS = pos;
    output.texcoord   = DYNAMIC_SCALING_APPLY_SCALEBIAS(uv);

    return output;
}

#endif
