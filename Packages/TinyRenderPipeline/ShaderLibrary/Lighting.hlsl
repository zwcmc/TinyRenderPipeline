#ifndef TINY_RP_LIGHTING_INCLUDED
#define TINY_RP_LIGHTING_INCLUDED

#include "Packages/com.tiny.render-pipeline/ShaderLibrary/RealtimeLights.hlsl"

float3 ShadingLit(Light light, BRDFData brdfData, InputData inputData)
{
    float3 N = inputData.normalWS;
    float3 V = inputData.viewDirectionWS;
    float3 L = normalize(light.direction);
    float3 H = normalize(L + V);

    float NoL = saturate(dot(N, L));
    float NoV = saturate(dot(N, V));
    float NoH = saturate(dot(N, H));
    float LoH = saturate(dot(L, H));

    float roughness = brdfData.roughness;

    float3 radiance = light.color * light.distanceAttenuation * light.shadowAttenuation * NoL;

    float3 diffuseColor = brdfData.diffuseColor;
    float3 f0 = brdfData.f0;

    float3 Fd = diffuseColor * Fd_Burley(roughness, NoV, NoL, LoH);

    float D = D_GGX(roughness, NoH);
    float Vis = V_SmithGGXCorrelated(roughness, NoV, NoL);
    float3 F = F_Schlick(f0, LoH);
    float3 Fr = (D * Vis) * F;

    return (Fd + Fr) * radiance;
}

float3 ShadingIndirect(BRDFData brdfData, InputData inputData)
{
    float3 N = inputData.normalWS;
    float3 V = inputData.viewDirectionWS;

    float NoV = saturate(dot(N, V));

    float roughness = brdfData.roughness;

    // IBL
    float3 diffuseIrradiance = Irradiance_SphericalHarmonics(N);

    float3 r = reflect(-V, N);
    float3 prefilteredRadiance = PrefilteredRadiance(r, roughness);

    float3 dfg = PrefilteredDFG_LUT(NoV, roughness);
    float3 E = brdfData.f0 * dfg.x + dfg.y;

    float3 iblFr = E * prefilteredRadiance;
    float3 iblFd = brdfData.diffuseColor * diffuseIrradiance * (1.0 - E);

    return iblFd + iblFr;
}

float4 SurfaceShading(InputData inputData, SurfaceData surfaceData)
{
    uint meshRenderingLayers = GetMeshRenderingLayer();

    // Light mainLight = GetMainLight(inputData);

    BRDFData brdfData;
    InitializeBRDFData(surfaceData, brdfData);

    Light mainLight = GetMainLight(inputData);

    float3 color = 0.0;
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
    {
        color += ShadingLit(mainLight, brdfData, inputData);
    }

    // IBL
    color += ShadingIndirect(brdfData, inputData);

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
