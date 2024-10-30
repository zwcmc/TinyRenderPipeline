#ifndef TINY_RP_SHADER_VARIABLES_FUNCTIONS_INCLUDED
#define TINY_RP_SHADER_VARIABLES_FUNCTIONS_INCLUDED

float2 GetNormalizedScreenSpaceUV(float4 positionCS)
{
    float2 normalizedScreenSpaceUV = positionCS.xy * rcp(_ScreenParams.xy);
#ifdef UNITY_UV_STARTS_AT_TOP
    normalizedScreenSpaceUV.y = 1.0 - normalizedScreenSpaceUV.y;
#endif
    return normalizedScreenSpaceUV;
}

// These are expected to align with the commonly used "_Surface" material property
static const half kSurfaceTypeOpaque = 0.0;
static const half kSurfaceTypeTransparent = 1.0;

// Could be e.g. the position of a primary camera or a shadow-casting light.
float3 GetCurrentViewPosition()
{
    return _WorldSpaceCameraPos;
}

// Returns 'true' if the current view performs a perspective projection.
bool IsPerspectiveProjection()
{
    return (unity_OrthoParams.w == 0);
}

float3 GetCameraPositionWS()
{
    return _WorldSpaceCameraPos;
}

// Returns the forward (central) direction of the current view in the world space.
float3 GetViewForwardDir()
{
    float4x4 viewMat = GetWorldToViewMatrix();
    return -viewMat[2].xyz;
}

half3 GetWorldSpaceNormalizeViewDir(float3 positionWS)
{
    if (IsPerspectiveProjection())
    {
        // Perspective
        float3 V = GetCurrentViewPosition() - positionWS;
        return half3(normalize(V));
    }
    else
    {
        // Orthographic
        return half3(-GetViewForwardDir());
    }
}

// Returns true if the input value represents an opaque surface
bool IsSurfaceTypeOpaque(half surfaceType)
{
    return (surfaceType == kSurfaceTypeOpaque);
}

// Returns true if the input value represents a transparent surface
bool IsSurfaceTypeTransparent(half surfaceType)
{
    return (surfaceType == kSurfaceTypeTransparent);
}

real AlphaDiscard(real alpha, real cutoff)
{
#ifdef _ALPHATEST_ON
    alpha = (alpha >= cutoff) ? alpha : 0.0;
    clip(alpha - 0.0001);
#endif
    return alpha;
}

half OutputAlpha(half alpha, bool isTransparent)
{
    return isTransparent ? alpha : 1.0;
}

float3 NormalizeNormalPerPixel(float3 normalWS)
{
#if defined(UNITY_NO_DXT5nm) && defined(_NORMALMAP)
    return SafeNormalize(normalWS);
#else
    return normalize(normalWS);
#endif
}

uint GetMeshRenderingLayer()
{
    return asuint(unity_RenderingLayer.x);
}

float LinearDepthToEyeDepth(float rawDepth)
{
#if defined(UNITY_REVERSED_Z)
    return _ProjectionParams.z - (_ProjectionParams.z - _ProjectionParams.y) * rawDepth;
#else
    return _ProjectionParams.y + (_ProjectionParams.z - _ProjectionParams.y) * rawDepth;
#endif
}

// Select uint4 component by index.
// Helper to improve codegen for 2d indexing (data[x][y])
// Replace:
// data[i / 4][i % 4];
// with:
// select4(data[i / 4], i % 4);
uint Select4(uint4 v, uint i)
{
    // x = 0 = 00
    // y = 1 = 01
    // z = 2 = 10
    // w = 3 = 11
    uint mask0 = uint(int(i << 31) >> 31);
    uint mask1 = uint(int(i << 30) >> 31);
    return
        (((v.w & mask0) | (v.z & ~mask0)) & mask1) |
        (((v.y & mask0) | (v.x & ~mask0)) & ~mask1);
}

#if SHADER_TARGET < 45
uint URP_FirstBitLow(uint m)
{
    // http://graphics.stanford.edu/~seander/bithacks.html#ZerosOnRightFloatCast
    return (asuint((float)(m & asuint(-asint(m)))) >> 23) - 0x7F;
}
#define FIRST_BIT_LOW URP_FirstBitLow
#else
#define FIRST_BIT_LOW firstbitlow
#endif

#endif
