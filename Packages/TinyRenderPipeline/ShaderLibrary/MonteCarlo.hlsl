#ifndef TINY_RP_MONTE_CARLO_INCLUDED
#define TINY_RP_MONTE_CARLO_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Macros.hlsl"
#include "CommonMath.hlsl"

float2 Hammersley(uint i, uint numSamples)
{
    const float tof = 0.5 / 0x80000000U;  // 2.3283064365386963e-10
    const float invNumSamples = 1.0 / (float)numSamples;
    uint bits = i;
    bits = (bits << 16u) | (bits >> 16u);
    bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
    bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
    bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
    bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
    return float2(i * invNumSamples, (float)bits * tof);
}

float3 HemisphereImportanceSampleDggx(float2 u, float a)  // pdf = D(a) * cosTheta
{
    const float phi = 2 * PI * u.x;
    // NOTE: (a2-1) == (a-1)(a+1) produces better fp accuracy
    const float cosTheta2 = (1 - u.y) / (1 + (a + 1) * ((a - 1) * u.y));
    const float cosTheta = sqrt(cosTheta2);
    const float sinTheta = sqrt(1 - cosTheta2);
    return float3(sinTheta * cos(phi), sinTheta * sin(phi), cosTheta);
}

static float Visibility(float NoV, float NoL, float a) {
    // Heitz 2014, "Understanding the Masking-Shadowing Function in Microfacet-Based BRDFs"
    // Height-correlated GGX
    const float a2 = a * a;
    const float GGXL = NoV * sqrt((NoL - NoL * a2) * NoL + a2);
    const float GGXV = NoL * sqrt((NoV - NoV * a2) * NoV + a2);
    return 0.5 / (GGXV + GGXL);
}

/*
 *
 * Importance sampling GGX - Trowbridge-Reitz
 * ------------------------------------------
 *
 * Important samples are chosen to integrate Dggx() * cos(theta) over the hemisphere.
 *
 * All calculations are made in tangent space, with n = [0 0 1]
 *
 *                      h (important sample)
 *                     /.
 *                    / .
 *                   /  .
 *                  /   .
 *         --------o----+-------> n
 *                   cos(theta)
 *                    = n•h
 *
 *  h is micro facet's normal
 *  l is the reflection of v around h, l = reflect(-v, h)  ==>  v•h = l•h
 *
 *  n•v is given as an input parameter at runtime
 *
 *  Since n = [0 0 1], we also have v.z = n•v
 *
 *  Since we need to compute v•h, we chose v as below. This choice only affects the
 *  computation of v•h (and therefore the fresnel term too), but doesn't affect
 *  n•l, which only relies on l.z (which itself only relies on v.z, i.e.: n•v)
 *
 *      | sqrt(1 - (n•v)^2)     (sin)
 *  v = | 0
 *      | n•v                   (cos)
 *
 *
 *  h = important_sample_ggx()
 *
 *  l = reflect(-v, h) = 2 * v•h * h - v;
 *
 *  n•l = [0 0 1] • l = l.z
 *
 *  n•h = [0 0 1] • l = h.z
 *
 *
 *  pdf() = D(h) <n•h> |J(h)|
 *
 *               1
 *  |J(h)| = ----------
 *            4 <v•h>
 *
 *
 * Evaluating the integral
 * -----------------------
 *
 * We are trying to evaluate the following integral:
 *
 *                    /
 *             Er() = | fr(s) <n•l> ds
 *                    /
 *                    Ω
 *
 * For this, we're using importance sampling:
 *
 *                    1     fr(h)
 *            Er() = --- ∑ ------- <n•l>
 *                    N  h   pdf
 *
 * with:
 *
 *            fr() = D(h) F(h) V(v, l)
 *
 *
 *  It results that:
 *
 *            1                        4 <v•h>
 *    Er() = --- ∑ D(h) F(h) V(v, l) ------------ <n•l>
 *            N  h                     D(h) <n•h>
 *
 *
 *  +-------------------------------------------+
 *  |          4                  <v•h>         |
 *  |  Er() = --- ∑ F(h) V(v, l) ------- <n•l>  |
 *  |          N  h               <n•h>         |
 *  +-------------------------------------------+
 *
 */
float2 DFV(float NoV, float roughness)
{
    float2 r = 0.0;
    const uint numSamples = 1024;

    // 由于各向同性，随意取一个 V 即可
    float3 V = float3(sqrt(1.0 - NoV * NoV), 0.0, NoV);

    for (uint i = 0; i < numSamples; ++i)
    {
        const float2 u = Hammersley(i, numSamples);
        const float3 H = HemisphereImportanceSampleDggx(u, roughness);
        const float3 L = 2 * dot(V, H) * H - V;
        const float VoH = saturate(dot(V, H));
        const float NoL = saturate(L.z);
        const float NoH = saturate(H.z);

        if (NoL > 0)
        {
            /*
             * Fc = (1 - V•H)^5
             * F(h) = f0*(1 - Fc) + f90*Fc
             *
             * f0 and f90 are known at runtime, but thankfully can be factored out, allowing us
             * to split the integral in two terms and store both terms separately in a LUT.
             *
             * At runtime, we can reconstruct Er() exactly as below:
             *
             *            4                      <v•h>
             *   DFV.x = --- ∑ (1 - Fc) V(v, l) ------- <n•l>
             *            N  h                   <n•h>
             *
             *
             *            4                      <v•h>
             *   DFV.y = --- ∑ (    Fc) V(v, l) ------- <n•l>
             *            N  h                   <n•h>
             *
             *
             *   Er() = f0 * DFV.x + f90 * DFV.y
             *
             */
            const float v = Visibility(NoV, NoL, roughness) * NoL * (VoH / NoH);
            const float Fc = pow5(1 - VoH);
            r.x += v * (1.0 - Fc);
            r.y += v * Fc;
        }
    }

    return r * (4.0 / numSamples);
}

#endif
