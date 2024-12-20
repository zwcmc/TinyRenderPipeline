#ifndef TINY_RP_LIGHTING_INCLUDED
#define TINY_RP_LIGHTING_INCLUDED

#include "Packages/com.zwcmc.tiny-rp/ShaderLibrary/RealtimeLights.hlsl"

// Screen space ambient occlusion
TEXTURE2D(_ScreenSpaceOcclusionTexture);      SAMPLER(sampler_ScreenSpaceOcclusionTexture);

// Screen space reflection
TEXTURE2D(_SsrLightingTexture);                  SAMPLER(sampler_SsrLightingTexture);

half SampleAmbientOcclusion(float2 normalizedScreenSpaceUV)
{
    half ao = SAMPLE_TEXTURE2D_LOD(_ScreenSpaceOcclusionTexture, sampler_ScreenSpaceOcclusionTexture, normalizedScreenSpaceUV, 0.0).r;
    return ao;
}

half3 ShadingLit(Light light, BRDFData brdfData, InputData inputData)
{
    half3 N = inputData.normalWS;
    half3 V = inputData.viewDirectionWS;
    half3 L = normalize(light.direction);
    half3 H = normalize(L + V);

    half NoL = saturate(dot(N, L));
    half NoV = saturate(dot(N, V));
    float NoH = saturate(dot(N, H));
    half LoH = saturate(dot(L, H));

    half roughness = brdfData.roughness;

    half3 radiance = light.color * light.distanceAttenuation * light.shadowAttenuation * NoL;

    // kd
    half3 diffuseColor = brdfData.diffuseColor;

    // fd
    half3 Fd = diffuseColor * Fd_Burley(roughness, NoV, NoL, LoH);

    // fs
    half3 f0 = brdfData.f0;
    float D = D_GGX(roughness, NoH);
    half G = V_SmithGGXCorrelated(roughness, NoV, NoL);
    half3 F = F_Schlick(f0, LoH);
    half3 Fs = (D * G) * F;

    return (Fd + Fs) * radiance;
}

half3 ShadingIndirect(BRDFData brdfData, InputData inputData, half diffuseAO)
{
    half3 N = inputData.normalWS;
    half3 V = inputData.viewDirectionWS;

    float NoV = saturate(dot(N, V));
    float roughness = brdfData.roughness;

    half3 diffuseIrradiance = Irradiance_SphericalHarmonics(N);

    float3 r = reflect(-V, N);
    half3 prefilteredRadiance = PrefilteredRadiance(r, roughness);

    half3 dfg = PrefilteredDFG_LUT(NoV, roughness);
    half3 E = brdfData.f0 * dfg.x + dfg.y;
    half3 iblFr = E * prefilteredRadiance;

    // Ssr lighting
    float mipLevel = lerp(0, 8, brdfData.perceptualRoughness);  // simple lerp
    half4 ssrLighting = SAMPLE_TEXTURE2D_LOD(_SsrLightingTexture, sampler_SsrLightingTexture, inputData.normalizedScreenSpaceUV, mipLevel);
    float envWeight = 1.0 - ssrLighting.a;
    iblFr = iblFr * envWeight + (E * ssrLighting);

    half3 iblFd = brdfData.diffuseColor * diffuseIrradiance * diffuseAO;

    return iblFr + iblFd;
}

half4 SurfaceShading(InputData inputData, SurfaceData surfaceData)
{
    uint meshRenderingLayers = GetMeshRenderingLayer();

    BRDFData brdfData;
    InitializeBRDFData(surfaceData, brdfData);

    Light mainLight = GetMainLight(inputData);

    half3 color = 0.0;
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
    {
        color += ShadingLit(mainLight, brdfData, inputData);
    }

    // IBL
    half occlusion = SampleAmbientOcclusion(inputData.normalizedScreenSpaceUV);
    color += ShadingIndirect(brdfData, inputData, occlusion);

    uint additionalLightCount = GetAdditionalLightsCount();
    for (uint lightIndex = 0u; lightIndex < additionalLightCount; ++lightIndex)
    {
        Light light = GetAdditionalLight(lightIndex, inputData.positionWS);
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
        {
            color += ShadingLit(light, brdfData, inputData);
        }
    }

    return half4(color, surfaceData.alpha);
}

#endif
