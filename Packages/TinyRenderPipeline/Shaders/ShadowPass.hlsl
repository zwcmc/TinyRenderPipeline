#ifndef TINY_RP_SHADOW_PASS_INCLUDED
#define TINY_RP_SHADOW_PASS_INCLUDED

#include "Packages/com.zwcmc.tiny-rp/ShaderLibrary/Core.hlsl"

// For directional light, xyz: light direction, w: 1.0
// For spot light and point light, xyz: light position, w: 0.0
float4 _LightDirectionOrPosition;

float4 _ShadowBias; // x: depth bias, y: normal bias

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
};

float3 ApplyShadowBias(float3 positionWS, float3 normalWS, float3 lightDirection)
{
    float invNoL = 1.0 - saturate(dot(lightDirection, normalWS));
    float scale = invNoL * _ShadowBias.y;

    positionWS = lightDirection * _ShadowBias.xxx + positionWS;
    positionWS = normalWS * scale.xxx + positionWS;
    return positionWS;
}

float4 GetShadowPositionHClip(Attributes input)
{
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
    float3 lightDirection = _LightDirectionOrPosition.w > 0.5 ? _LightDirectionOrPosition.xyz : normalize(_LightDirectionOrPosition.xyz - positionWS);

    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));

#if defined(UNITY_REVERSED_Z)
    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif

    return positionCS;
}

float4 ShadowVertex(Attributes input) : SV_POSITION
{
    return GetShadowPositionHClip(input);
}

half4 ShadowFragment() : SV_Target0
{
    return 0;
}

#endif
