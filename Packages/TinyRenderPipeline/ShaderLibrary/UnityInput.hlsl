#ifndef TINY_RP_UNITY_INPUT_INCLUDED
#define TINY_RP_UNITY_INPUT_INCLUDED

// Block Layout should be respected due to SRP Batcher
CBUFFER_START(UnityPerDraw)
// Space block Feature
float4x4 unity_ObjectToWorld;
float4x4 unity_WorldToObject;
float4 unity_LODFade; // x is the fade value ranging within [0,1]. y is x quantized into 16 levels
real4 unity_WorldTransformParams; // w is usually 1.0, or -1.0 for odd-negative scale transforms

// Render Layer block feature
// Only the first channel (x) contains valid data and the float must be reinterpreted using asuint() to extract the original 32 bits values.
float4 unity_RenderingLayer;

// Light Indices block feature
// These are set internally by the engine upon request by RendererConfiguration.
half4 unity_LightData;
half4 unity_LightIndices[2];

// Reflection Probe 0 block feature
// HDR environment map decode instructions
real4 unity_SpecCube0_HDR;

// SH block feature
real4 unity_SHAr;
real4 unity_SHAg;
real4 unity_SHAb;
real4 unity_SHBr;
real4 unity_SHBg;
real4 unity_SHBb;
real4 unity_SHC;
CBUFFER_END

float4x4 unity_MatrixVP;
float4x4 unity_MatrixV;
float4x4 unity_MatrixInvV;
float4x4 unity_prev_MatrixM;
float4x4 unity_prev_MatrixIM;
float4x4 glstate_matrix_projection;

float3 _WorldSpaceCameraPos;

// x = orthographic camera's width
// y = orthographic camera's height
// z = unused
// w = 1.0 if camera is ortho, 0.0 if perspective
float4 unity_OrthoParams;

// Reflection cube
TEXTURECUBE(unity_SpecCube0);
SAMPLER(samplerunity_SpecCube0);

#endif
