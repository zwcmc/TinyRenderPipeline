#ifndef TINY_RP_INPUT_INCLUDED
#define TINY_RP_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.tiny.render-pipeline/ShaderLibrary/UnityInput.hlsl"

// Max visible additional lights count
#define MAX_VISIBLE_LIGHTS 8

struct InputData
{
    float3 positionWS;
    float3 normalWS;
    half3 viewDirectionWS;
    float4 shadowCoord;
    half3 bakedGI;
};

// Main light
float4 _MainLightPosition;
half4 _MainLightColor;
uint _MainLightLayerMask;

half4 _AdditionalLightsCount;

#ifndef SHADER_API_GLES3
CBUFFER_START(AdditionalLights)
#endif
float4 _AdditionalLightsPosition[MAX_VISIBLE_LIGHTS];
float4 _AdditionalLightsAttenuation[MAX_VISIBLE_LIGHTS];
half4 _AdditionalLightsColor[MAX_VISIBLE_LIGHTS];
half4 _AdditionalLightsSpotDir[MAX_VISIBLE_LIGHTS];
float _AdditionalLightsLayerMasks[MAX_VISIBLE_LIGHTS];
#ifndef SHADER_API_GLES3
CBUFFER_END
#endif

#define UNITY_MATRIX_M     unity_ObjectToWorld
#define UNITY_MATRIX_I_M   unity_WorldToObject
#define UNITY_MATRIX_V     unity_MatrixV
#define UNITY_MATRIX_I_V   unity_MatrixInvV
#define UNITY_MATRIX_P     glstate_matrix_projection
#define UNITY_MATRIX_VP    unity_MatrixVP
#define UNITY_MATRIX_I_VP  unity_MatrixInvVP
#define UNITY_PREV_MATRIX_M   unity_MatrixPreviousM
#define UNITY_PREV_MATRIX_I_M unity_MatrixPreviousMI

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

#endif
