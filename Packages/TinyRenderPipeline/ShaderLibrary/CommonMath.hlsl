#ifndef TINY_RP_COMMON_MATH_INCLUDED
#define TINY_RP_COMMON_MATH_INCLUDED

float sq(float x)
{
    return x * x;
}

// Computes x^5 using only multiply operations.
float pow5(float x)
{
    float x2 = x * x;
    return x2 * x2 * x;
}

float max3(float3 v)
{
    return max(v.x, max(v.y, v.z));
}

/*
 * Random number between 0 and 1, using interleaved gradient noise.
 * w must not be normalized (e.g. window coordinates)
 */
float InterleavedGradientNoise(float2 w)
{
    const float3 m = float3(0.06711056, 0.00583715, 52.9829189);
    return frac(m.z * frac(dot(w, m.xy)));
}

#endif
