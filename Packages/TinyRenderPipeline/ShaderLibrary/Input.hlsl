#ifndef TINY_RP_INPUT_INCLUDED
#define TINY_RP_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.tiny.render-pipeline/ShaderLibrary/UnityInput.hlsl"

// Max visible additional lights count
#define MAX_VISIBLE_LIGHTS 8
#define MAX_SHADOW_SLICE_COUNT 48

struct InputData
{
    float3 positionWS;
    float3 normalWS;
    half3 viewDirectionWS;
    float4 shadowCoord;
    half3 bakedGI;
    float2 normalizedScreenSpaceUV;
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

// Forward+ Rendering Path

// Match with values in TinyRenderPipeline.cs
#define MAX_ZBIN_VEC4S 1024
#define MAX_TILE_VEC4S 1024

#ifdef  _FORWARD_PLUS
CBUFFER_START(trp_ZBinBuffer)
float4 urp_ZBins[MAX_ZBIN_VEC4S];
CBUFFER_END
CBUFFER_START(trp_TileBuffer)
float4 urp_Tiles[MAX_TILE_VEC4S];
CBUFFER_END

float4 _FPParams0;
float4 _FPParams1;
// float4 _FPParams2;

#define URP_FP_ZBIN_SCALE (_FPParams0.x)
#define URP_FP_ZBIN_OFFSET (_FPParams0.y)
#define URP_FP_PROBES_BEGIN ((uint)_FPParams0.z)
// Directional lights would be in all clusters, so they don't go into the cluster structure.
// Instead, they are stored first in the light buffer.
#define URP_FP_DIRECTIONAL_LIGHTS_COUNT ((uint)_FPParams0.w)

// Scale from screen-space UV [0, 1] to tile coordinates [0, tile resolution].
#define URP_FP_TILE_SCALE ((float2)_FPParams1.xy)
#define URP_FP_TILE_COUNT_X ((uint)_FPParams1.z)
#define URP_FP_WORDS_PER_TILE ((uint)_FPParams1.w)

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
