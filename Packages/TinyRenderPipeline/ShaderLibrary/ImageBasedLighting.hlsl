#ifndef TINY_RP_IMAGE_BASED_LIGHTING_INCLUDED
#define TINY_RP_IMAGE_BASED_LIGHTING_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SphericalHarmonics.hlsl"

#ifndef UNITY_SPECCUBE_LOD_STEPS
    // This is actuall the last mip index, we generate 7 mips of convolution
    #define UNITY_SPECCUBE_LOD_STEPS 6
#endif

half PerceptualRoughnessToLod(half perceptualRoughness)
{
    return UNITY_SPECCUBE_LOD_STEPS * perceptualRoughness * (1.7 - 0.7 * perceptualRoughness);
}

half3 DecodeHDREnvironment(half4 encodedIrradiance, half4 decodeInstructions)
{
    // Take into account texture alpha if decodeInstructions.w is true(the alpha value affects the RGB channels)
    half alpha = max(decodeInstructions.w * (encodedIrradiance.a - 1.0) + 1.0, 0.0);

    // If Linear mode is not supported we can skip exponent part
    return (decodeInstructions.x * PositivePow(alpha, decodeInstructions.y)) * encodedIrradiance.rgb;
}

// Samples SH L0, L1 and L2 terms
half3 Irradiance_SphericalHarmonics(half3 normalWS)
{
    real4 SHCoefficients[7];
    SHCoefficients[0] = unity_SHAr;
    SHCoefficients[1] = unity_SHAg;
    SHCoefficients[2] = unity_SHAb;
    SHCoefficients[3] = unity_SHBr;
    SHCoefficients[4] = unity_SHBg;
    SHCoefficients[5] = unity_SHBb;
    SHCoefficients[6] = unity_SHC;

    return max(half3(0, 0, 0), SampleSH9(SHCoefficients, normalWS));
}

half3 PrefilteredRadiance(float3 r, half perceptualRoughness)
{
    half lod = PerceptualRoughnessToLod(perceptualRoughness);
    half3 radiance = DecodeHDREnvironment(SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, r, lod), unity_SpecCube0_HDR);
    return radiance;
}

half3 PrefilteredDFG_LUT(float NoV, float lod)
{
    return SAMPLE_TEXTURE2D_LOD(_IBL_DFG, sampler_IBL_DFG, float2(NoV, lod), 0.0).rgb;
}

#endif
