#ifndef TINY_RP_SHADER_VARIABLES_FUNCTIONS_INCLUDED
#define TINY_RP_SHADER_VARIABLES_FUNCTIONS_INCLUDED

// These are expected to align with the commonly used "_Surface" material property
static const half kSurfaceTypeOpaque = 0.0;
static const half kSurfaceTypeTransparent = 1.0;

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

#endif
