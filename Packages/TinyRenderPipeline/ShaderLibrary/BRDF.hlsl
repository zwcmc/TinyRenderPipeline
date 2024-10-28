#ifndef TINY_RP_BRDF_INCLUDED
#define TINY_RP_BRDF_INCLUDED

#define kDielectricSpec float4(0.04, 0.04, 0.04, 1.0 - 0.04) // standard dielectric reflectivity coef at incident angle (= 4%)
#define kMinPerceptualRoughness 0.089

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
    outBRDFData.perceptualRoughness = clamp(PerceptualSmoothnessToPerceptualRoughness(surfaceData.smoothness), kMinPerceptualRoughness, 1.0);
    outBRDFData.roughness = PerceptualRoughnessToRoughness(outBRDFData.perceptualRoughness);
}

//-----------------------------------------------------------------------------
// Fresnel term implementations
//-----------------------------------------------------------------------------

// Schlick 1994, "An Inexpensive BRDF Model for Physically-Based Rendering"
float3 F_Schlick(const float3 f0, float f90, float VoH)
{
    float x = 1.0 - VoH;
    float x5 = pow5(x);
    return (f90 - f0) * x5 + f0;
}

float3 F_Schlick(const float3 f0, float VoH)
{
    return F_Schlick(f0, 1.0, VoH);
}

float F_Schlick(float f0, float f90, float VoH)
{
    float x = 1.0 - VoH;
    float x5 = pow5(x);
    return (f90 - f0) * x5 + f0;
}

//------------------------------------------------------------------------------
// Diffuse BRDF implementations
//------------------------------------------------------------------------------

float Fd_Lambert()
{
    return INV_PI;
}

// Burley 2012, "Physically-Based Shading at Disney"
float Fd_Burley(float roughness, float NoV, float NoL, float LoH)
{
    float f90 = 0.5 + 2.0 * roughness * LoH * LoH;
    float lightScatter = F_Schlick(1.0, f90, NoL);
    float viewScatter = F_Schlick(1.0, f90, NoV);
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

half D_GGX_Mobile(half roughness, half NoH, half3 N, half3 H)
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
float V_SmithGGXCorrelated(float roughness, float NoV, float NoL)
{
    float a2 = roughness * roughness;

    float lambdaV = NoL * sqrt((NoV - a2 * NoV) * NoV + a2);
    float lambdaL = NoV * sqrt((NoL - a2 * NoL) * NoL + a2);
    float v = 0.5 / (lambdaV + lambdaL);
    // a2=0 => v = 1 / 4*NoL*NoV   => min=1/4, max=+inf
    // a2=1 => v = 1 / 2*(NoL+NoV) => min=1/4, max=+inf
    return min(v, HALF_MAX);
}

// Hammon 2017, "PBR Diffuse Lighting for GGX+Smith Microsurfaces"
float V_SmithGGXCorrelated_Fast(float roughness, float NoV, float NoL)
{
    float v = 0.5 / lerp(2.0 * NoL * NoV, NoL + NoV, roughness);
    return min(v, HALF_MAX);
}

// struct BRDFData
// {
//     half3 diffuse;
//     half3 specular;
//     half perceptualRoughness;
//     half roughness;
//     half roughness2;
//     half grazingTerm;
//
//     // We save some light invariant BRDF terms so we don't have to recompute
//     // them in the light loop. Take a look at DirectBRDF function for detailed explaination.
//     half normalizationTerm;     // roughness * 4.0 + 2.0
//     half roughness2MinusOne;    // roughness^2 - 1.0
// };
//
// half OneMinusReflectivityMetallic(half metallic)
// {
//     // We'll need oneMinusReflectivity, so
//     //   1-reflectivity = 1-lerp(dielectricSpec, 1, metallic) = lerp(1-dielectricSpec, 0, metallic)
//     // store (1-dielectricSpec) in kDielectricSpec.a, then
//     //   1-reflectivity = lerp(alpha, 0, metallic) = alpha + metallic*(0 - alpha) =
//     //                  = alpha - metallic * alpha
//     half oneMinusDielectricSpec = kDielectricSpec.a;
//     return oneMinusDielectricSpec - metallic * oneMinusDielectricSpec;
// }
//
// inline void InitializeBRDFData(SurfaceData surfaceData, out BRDFData outBRDFData)
// {
//     outBRDFData = (BRDFData)0;
//
//     half oneMinusReflectivity = OneMinusReflectivityMetallic(surfaceData.metallic);
//     half reflectivity = half(1.0) - oneMinusReflectivity;
//
//     outBRDFData.diffuse = surfaceData.albedo * oneMinusReflectivity;
//     outBRDFData.specular = lerp(kDielectricSpec.rgb, surfaceData.albedo, surfaceData.metallic);
//
//     outBRDFData.perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(surfaceData.smoothness);
//     outBRDFData.roughness = max(PerceptualRoughnessToRoughness(outBRDFData.perceptualRoughness), HALF_MIN_SQRT);
//     outBRDFData.roughness2 = max(outBRDFData.roughness * outBRDFData.roughness, HALF_MIN);
//     outBRDFData.grazingTerm = saturate(surfaceData.smoothness + reflectivity);
//     outBRDFData.normalizationTerm = outBRDFData.roughness * half(4.0) + half(2.0);
//     outBRDFData.roughness2MinusOne  = outBRDFData.roughness2 - half(1.0);
// }
//
// half DirectBRDFSpecular(BRDFData brdfData, half3 normalWS, half3 lightDirectionWS, half3 viewDirectionWS)
// {
//     float3 lightDirectionWSFloat3 = float3(lightDirectionWS);
//     float3 halfDir = SafeNormalize(lightDirectionWSFloat3 + float3(viewDirectionWS));
//
//     float NoH = saturate(dot(float3(normalWS), halfDir));
//     half LoH = half(saturate(dot(lightDirectionWSFloat3, halfDir)));
//
//     // GGX Distribution multiplied by combined approximation of Visibility and Fresnel
//     // BRDFspec = (D * V * F) / 4.0
//     // D = roughness^2 / ( NoH^2 * (roughness^2 - 1) + 1 )^2
//     // V * F = 1.0 / ( LoH^2 * (roughness + 0.5) )
//     // See "Optimizing PBR for Mobile" from Siggraph 2015 moving mobile graphics course
//     // https://community.arm.com/events/1155
//
//     // Final BRDFspec = roughness^2 / ( NoH^2 * (roughness^2 - 1) + 1 )^2 * (LoH^2 * (roughness + 0.5) * 4.0)
//     // We further optimize a few light invariant terms
//     // brdfData.normalizationTerm = (roughness + 0.5) * 4.0 rewritten as roughness * 4.0 + 2.0 to a fit a MAD.
//     float d = NoH * NoH * brdfData.roughness2MinusOne + 1.00001f;
//
//     half LoH2 = LoH * LoH;
//     half specularTerm = brdfData.roughness2 / ((d * d) * max(0.1h, LoH2) * brdfData.normalizationTerm);
//
//     // On platforms where half actually means something, the denominator has a risk of overflow
//     // clamp below was added specifically to "fix" that, but dx compiler (we convert bytecode to metal/gles)
//     // sees that specularTerm have only non-negative terms, so it skips max(0,..) in clamp (leaving only min(100,...))
// #if defined(REAL_IS_HALF)
//     specularTerm = specularTerm - HALF_MIN;
//     // Update: Conservative bump from 100.0 to 1000.0 to better match the full float specular look.
//     // Roughly 65504.0 / 32*2 == 1023.5,
//     // or HALF_MAX / ((mobile) MAX_VISIBLE_LIGHTS * 2),
//     // to reserve half of the per light range for specular and half for diffuse + indirect + emissive.
//     specularTerm = clamp(specularTerm, 0.0, 1000.0); // Prevent FP16 overflow on mobiles
// #endif
//
//     return specularTerm;
// }
//
// half3 EnvironmentBRDFSpecular(BRDFData brdfData, half fresnelTerm)
// {
//     float surfaceReduction = 1.0 / (brdfData.roughness2 + 1.0);
//     return half3(surfaceReduction * lerp(brdfData.specular, brdfData.grazingTerm, fresnelTerm));
// }
//
// half3 EnvironmentBRDF(BRDFData brdfData, half3 indirectDiffuse, half3 indirectSpecular, half fresnelTerm)
// {
//     half3 c = indirectDiffuse * brdfData.diffuse;
//     c += indirectSpecular * EnvironmentBRDFSpecular(brdfData, fresnelTerm);
//     return c;
// }

#endif
