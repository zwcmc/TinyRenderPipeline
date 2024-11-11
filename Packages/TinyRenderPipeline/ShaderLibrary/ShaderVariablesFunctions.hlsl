#ifndef TINY_RP_SHADER_VARIABLES_FUNCTIONS_INCLUDED
#define TINY_RP_SHADER_VARIABLES_FUNCTIONS_INCLUDED

float2 GetNormalizedScreenSpaceUV(float4 positionCS)
{
    float2 normalizedScreenSpaceUV = positionCS.xy * rcp(_ScreenParams.xy);
// #ifdef UNITY_UV_STARTS_AT_TOP
//     normalizedScreenSpaceUV.y = 1.0 - normalizedScreenSpaceUV.y;
// #endif
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

float3 GetWorldSpaceNormalizeViewDir(float3 positionWS)
{
    if (IsPerspectiveProjection())
    {
        // Perspective
        float3 V = GetCurrentViewPosition() - positionWS;
        return normalize(V);
    }
    else
    {
        // Orthographic
        return -GetViewForwardDir();
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

#endif
