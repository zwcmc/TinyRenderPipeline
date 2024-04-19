#ifndef TINY_RP_PARTICLES_UNLIT_INPUT_INCLUDED
#define TINY_RP_PARTICLES_UNLIT_INPUT_INCLUDED

#include "Packages/com.tiny.render-pipeline/ShaderLibrary/Input.hlsl"
#include "Packages/com.tiny.render-pipeline/ShaderLibrary/ShaderVariablesFunctions.hlsl"

CBUFFER_START(UnityPerMaterial)
half4 _BaseColor;
float _CameraNearFadeDistance;
float _CameraFarFadeDistance;
float _SoftParticlesNearFadeDistance;
float _SoftParticlesFarFadeDistance;
half _DistortionBlend;
half _DistortionStrength;
half _Cutoff;
half _Surface;
CBUFFER_END

TEXTURE2D(_BaseMap);               SAMPLER(sampler_BaseMap);
TEXTURE2D(_DistortionNormal);      SAMPLER(sampler_DistortionNormal);

#endif
