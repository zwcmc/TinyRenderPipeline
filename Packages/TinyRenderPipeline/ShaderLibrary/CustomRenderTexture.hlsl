#ifndef TINY_RP_CUSTOM_RENDER_TEXTURE_INCLUDED
#define TINY_RP_CUSTOM_RENDER_TEXTURE_INCLUDED

#include "Packages/com.zwcmc.tiny-rp/ShaderLibrary/Core.hlsl"

// Keep in sync with CustomRenderTexture.h
#define kCustomTextureBatchSize 16

struct Attributes
{
    uint vertexID : SV_VertexID;
};

struct Varyings
{
    float4 vertex           : SV_POSITION;
    float3 localTexcoord    : TEXCOORD0;    // Texcoord local to the update zone (== globalTexcoord if no partial update zone is specified)
    float3 globalTexcoord   : TEXCOORD1;    // Texcoord relative to the complete custom texture
    uint primitiveID        : TEXCOORD2;    // Index of the update zone (correspond to the index in the updateZones of the Custom Texture)
    float3 direction        : TEXCOORD3;    // For cube textures, direction of the pixel being rendered in the cubemap
};

// Internal
float4      CustomRenderTextureCenters[kCustomTextureBatchSize];
float4      CustomRenderTextureSizesAndRotations[kCustomTextureBatchSize];
float       CustomRenderTexturePrimitiveIDs[kCustomTextureBatchSize];

float4      CustomRenderTextureParameters;
#define     CustomRenderTextureUpdateSpace  CustomRenderTextureParameters.x // Normalized(0)/PixelSpace(1)
#define     CustomRenderTexture3DTexcoordW  CustomRenderTextureParameters.y
#define     CustomRenderTextureIs3D         CustomRenderTextureParameters.z

// User facing uniform variables
float4      _CustomRenderTextureInfo; // x = width, y = height, z = depth, w = face/3DSlice

// Helpers
#define _CustomRenderTextureWidth   _CustomRenderTextureInfo.x
#define _CustomRenderTextureHeight  _CustomRenderTextureInfo.y
#define _CustomRenderTextureDepth   _CustomRenderTextureInfo.z

// Those two are mutually exclusive so we can use the same slot
#define _CustomRenderTextureCubeFace    _CustomRenderTextureInfo.w
#define _CustomRenderTexture3DSlice     _CustomRenderTextureInfo.w

float2 CustomRenderTextureRotate2D(float2 pos, float angle)
{
    float sn = sin(angle);
    float cs = cos(angle);

    return float2( pos.x * cs - pos.y * sn, pos.x * sn + pos.y * cs);
}

float3 CustomRenderTextureComputeCubeDirection(float2 globalTexcoord)
{
    float2 xy = globalTexcoord * 2.0 - 1.0;
    float3 direction;
    if(_CustomRenderTextureCubeFace == 0.0)
    {
        direction = normalize(float3(1.0, -xy.y, -xy.x));
    }
    else if(_CustomRenderTextureCubeFace == 1.0)
    {
        direction = normalize(float3(-1.0, -xy.y, xy.x));
    }
    else if(_CustomRenderTextureCubeFace == 2.0)
    {
        direction = normalize(float3(xy.x, 1.0, xy.y));
    }
    else if(_CustomRenderTextureCubeFace == 3.0)
    {
        direction = normalize(float3(xy.x, -1.0, -xy.y));
    }
    else if(_CustomRenderTextureCubeFace == 4.0)
    {
        direction = normalize(float3(xy.x, -xy.y, 1.0));
    }
    else if(_CustomRenderTextureCubeFace == 5.0)
    {
        direction = normalize(float3(-xy.x, -xy.y, -1.0));
    }

    return direction;
}

Varyings CustomRenderTextureVertex(Attributes input)
{
    Varyings output = (Varyings)0;

#if UNITY_UV_STARTS_AT_TOP
    const float2 vertexPositions[6] =
    {
        { -1.0f,  1.0f },
        { -1.0f, -1.0f },
        {  1.0f, -1.0f },
        {  1.0f,  1.0f },
        { -1.0f,  1.0f },
        {  1.0f, -1.0f }
    };

    const float2 texCoords[6] =
    {
        { 0.0f, 0.0f },
        { 0.0f, 1.0f },
        { 1.0f, 1.0f },
        { 1.0f, 0.0f },
        { 0.0f, 0.0f },
        { 1.0f, 1.0f }
    };
#else
    const float2 vertexPositions[6] =
    {
        {  1.0f,  1.0f },
        { -1.0f, -1.0f },
        { -1.0f,  1.0f },
        { -1.0f, -1.0f },
        {  1.0f,  1.0f },
        {  1.0f, -1.0f }
    };

    const float2 texCoords[6] =
    {
        { 1.0f, 1.0f },
        { 0.0f, 0.0f },
        { 0.0f, 1.0f },
        { 0.0f, 0.0f },
        { 1.0f, 1.0f },
        { 1.0f, 0.0f }
    };
#endif

    uint primitiveID = (input.vertexID / 6) % kCustomTextureBatchSize;
    uint vertexID = input.vertexID % 6;
    float3 updateZoneCenter = CustomRenderTextureCenters[primitiveID].xyz;
    float3 updateZoneSize = CustomRenderTextureSizesAndRotations[primitiveID].xyz;
    float rotation = CustomRenderTextureSizesAndRotations[primitiveID].w * PI / 180.0f;

#if !UNITY_UV_STARTS_AT_TOP
    rotation = -rotation;
#endif

    // Normalize rect if needed
    if (CustomRenderTextureUpdateSpace > 0.0) // Pixel space
    {
        // Normalize xy because we need it in clip space.
        updateZoneCenter.xy /= _CustomRenderTextureInfo.xy;
        updateZoneSize.xy /= _CustomRenderTextureInfo.xy;
    }
    else // normalized space
    {
        // Un-normalize depth because we need actual slice index for culling
        updateZoneCenter.z *= _CustomRenderTextureInfo.z;
        updateZoneSize.z *= _CustomRenderTextureInfo.z;
    }

    // Compute rotation

    // Compute quad vertex position
    float2 clipSpaceCenter = updateZoneCenter.xy * 2.0 - 1.0;
    float2 pos = vertexPositions[vertexID] * updateZoneSize.xy;
    pos = CustomRenderTextureRotate2D(pos, rotation);
    pos.x += clipSpaceCenter.x;
#if UNITY_UV_STARTS_AT_TOP
    pos.y += clipSpaceCenter.y;
#else
    pos.y -= clipSpaceCenter.y;
#endif

    // For 3D texture, cull quads outside of the update zone
    // This is neeeded in additional to the preliminary minSlice/maxSlice done on the CPU because update zones can be disjointed.
    // ie: slices [1..5] and [10..15] for two differents zones so we need to cull out slices 0 and [6..9]
    if (CustomRenderTextureIs3D > 0.0)
    {
        int minSlice = (int)(updateZoneCenter.z - updateZoneSize.z * 0.5);
        int maxSlice = minSlice + (int)updateZoneSize.z;
        if (_CustomRenderTexture3DSlice < minSlice || _CustomRenderTexture3DSlice >= maxSlice)
        {
            pos.xy = float2(1000.0, 1000.0); // Vertex outside of ncs
        }
    }

    output.vertex = float4(pos, UNITY_NEAR_CLIP_VALUE, 1.0);
    output.primitiveID = asuint(CustomRenderTexturePrimitiveIDs[primitiveID]);
    output.localTexcoord = float3(texCoords[vertexID], CustomRenderTexture3DTexcoordW);
    output.globalTexcoord = float3(pos.xy * 0.5 + 0.5, CustomRenderTexture3DTexcoordW);
#if UNITY_UV_STARTS_AT_TOP
    output.globalTexcoord.y = 1.0 - output.globalTexcoord.y;
#endif
    output.direction = CustomRenderTextureComputeCubeDirection(output.globalTexcoord.xy);

    return output;
}

#endif
