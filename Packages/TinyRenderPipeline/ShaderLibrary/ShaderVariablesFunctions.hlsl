#ifndef TINY_RP_SHADER_VARIABLES_FUNCTIONS_INCLUDED
#define TINY_RP_SHADER_VARIABLES_FUNCTIONS_INCLUDED

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
    if (isTransparent)
    {
        return alpha;
    }
    else
    {
        return 1.0;
    }
}

float3 NormalizeNormalPerPixel(float3 normalWS)
{
#if defined(UNITY_NO_DXT5nm) && defined(_NORMALMAP)
    return SafeNormalize(normalWS);
#else
    return normalize(normalWS);
#endif
}

#endif
