#ifndef TINY_RP_LIGHTING_INCLUDED
#define TINY_RP_LIGHTING_INCLUDED

#include "Packages/com.tiny.render-pipeline/ShaderLibrary/RealtimeLights.hlsl"

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

    half3 diffuseColor = brdfData.diffuseColor;
    half3 f0 = brdfData.f0;

    half3 Fd = diffuseColor * Fd_Burley(roughness, NoV, NoL, LoH);

    float D = D_GGX(roughness, NoH);
    half G = V_SmithGGXCorrelated(roughness, NoV, NoL);
    half3 F = F_Schlick(f0, LoH);
    half3 Fr = (D * G) * F;

    return (Fd + Fr) * radiance;
}

half3 ShadingIndirect(BRDFData brdfData, InputData inputData)
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
    half3 iblFd = brdfData.diffuseColor * diffuseIrradiance * (1.0 - E);

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
    color += ShadingIndirect(brdfData, inputData) * surfaceData.occlusion;

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
