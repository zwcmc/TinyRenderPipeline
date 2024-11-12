#ifndef TINY_RP_BRDF_INCLUDED
#define TINY_RP_BRDF_INCLUDED

#define kDielectricSpec float4(0.04, 0.04, 0.04, 1.0 - 0.04) // standard dielectric reflectivity coef at incident angle (= 4%)
// #define kMinPerceptualRoughness 0.089

struct BRDFData
{
    half3 diffuseColor;
    half3 f0;
    half perceptualRoughness;
    half roughness;
};

void InitializeBRDFData(SurfaceData surfaceData, out BRDFData outBRDFData)
{
    outBRDFData = (BRDFData)0;

    outBRDFData.diffuseColor = surfaceData.baseColor * (1.0 - surfaceData.metallic);
    outBRDFData.f0 = lerp(kDielectricSpec.rgb, surfaceData.baseColor, surfaceData.metallic);
    outBRDFData.perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(surfaceData.smoothness);
    outBRDFData.roughness = PerceptualRoughnessToRoughness(outBRDFData.perceptualRoughness);
}

//-----------------------------------------------------------------------------
// Fresnel term implementations
//-----------------------------------------------------------------------------

// Schlick 1994, "An Inexpensive BRDF Model for Physically-Based Rendering"
half3 F_Schlick(const half3 f0, half f90, half VoH)
{
    half x = 1.0 - VoH;
    half x5 = pow5(x);
    return (f90 - f0) * x5 + f0;
}

half3 F_Schlick(const half3 f0, half VoH)
{
    return F_Schlick(f0, 1.0, VoH);
}

half F_Schlick(half f0, half f90, half VoH)
{
    half x = 1.0 - VoH;
    half x5 = pow5(x);
    return (f90 - f0) * x5 + f0;
}

//------------------------------------------------------------------------------
// Diffuse BRDF implementations
//------------------------------------------------------------------------------

half Fd_Lambert()
{
    return INV_PI;
}

// Burley 2012, "Physically-Based Shading at Disney"
half Fd_Burley(half roughness, half NoV, half NoL, half LoH)
{
    half f90 = 0.5 + 2.0 * roughness * LoH * LoH;
    half lightScatter = F_Schlick(1.0, f90, NoL);
    half viewScatter = F_Schlick(1.0, f90, NoV);
    return lightScatter * viewScatter * INV_PI;
}

//------------------------------------------------------------------------------
// Specular BRDF implementations
//------------------------------------------------------------------------------

// Walter et al. 2007, "Microfacet Models for Refraction through Rough Surfaces"
float D_GGX(float roughness, float NoH)
{
    float oneMinusNoHSquared = 1.0 - NoH * NoH;

    float a = NoH * roughness;
    float k = roughness / (oneMinusNoHSquared + a * a);
    float d = k * k * INV_PI;
    return min(d, HALF_MAX);
}

half D_GGX_HalfPrecision(half roughness, half NoH, half3 N, half3 H)
{
    // In mediump, there are two problems computing 1.0 - NoH^2
    // 1) 1.0 - NoH^2 suffers floating point cancellation when NoH^2 is close to 1 (highlights)
    // 2) NoH doesn't have enough precision around 1.0
    // Both problem can be fixed by computing 1-NoH^2 in highp and providing NoH in highp as well

    // However, we can do better using Lagrange's identity:
    //      ||a x b||^2 = ||a||^2 ||b||^2 - (a . b)^2
    // since N and H are unit vectors: ||N x H||^2 = 1.0 - NoH^2
    // This computes 1.0 - NoH^2 directly (which is close to zero in the highlights and has
    // enough precision).
    // Overall this yields better performance, keeping all computations in mediump

    half3 NxH = cross(N, H);
    half oneMinusNoHSquared = dot(NxH, NxH);

    half a = NoH * roughness;
    half k = roughness / (oneMinusNoHSquared + a * a);
    half d = k * k * INV_PI;
    return min(d, HALF_MAX);
}

// Heitz 2014, "Understanding the Masking-Shadowing Function in Microfacet-Based BRDFs"
half V_SmithGGXCorrelated(half roughness, half NoV, half NoL)
{
    half a2 = roughness * roughness;

    half lambdaV = NoL * sqrt((NoV - a2 * NoV) * NoV + a2);
    half lambdaL = NoV * sqrt((NoL - a2 * NoL) * NoL + a2);
    half v = 0.5 / (lambdaV + lambdaL);
    // a2=0 => v = 1 / 4*NoL*NoV   => min=1/4, max=+inf
    // a2=1 => v = 1 / 2*(NoL+NoV) => min=1/4, max=+inf
    return min(v, HALF_MAX);
}

// Hammon 2017, "PBR Diffuse Lighting for GGX+Smith Microsurfaces"
half V_SmithGGXCorrelated_Fast(half roughness, half NoV, half NoL)
{
    half v = 0.5 / lerp(2.0 * NoL * NoV, NoL + NoV, roughness);
    return min(v, HALF_MAX);
}

#endif
