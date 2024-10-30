#ifndef TINY_RP_SHADOW_PASS_INCLUDED
#define TINY_RP_SHADOW_PASS_INCLUDED

// For directional light, xyz: light direction, w: 1.0
// For spot light and point light, xyz: light position, w: 0.0
float4 _LightDirectionOrPosition;

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
};

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

half4 ShadowFragment() : SV_Target
{
    return 0;
}

#endif
