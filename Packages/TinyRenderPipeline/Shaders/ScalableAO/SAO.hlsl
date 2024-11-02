#ifndef TINY_RP_SAO_INCLUDED
#define TINY_RP_SAO_INCLUDED

// #include "Packages/com.tiny.render-pipeline/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.tiny.render-pipeline/ShaderLibrary/CommonMath.hlsl"

#define BLUR_MAX_SAMPLE_COUNT 16
// Generated (in C#) with:
// const float standardDeviation = 4.0f;
// const int gaussianSampleCount = 16;
// float[] outKernel = new float[gaussianSampleCount];
// for (int i = 0; i < gaussianSampleCount; i++)
// {
//     float x = (float)i;
//     float g = Mathf.Exp(-(x * x) / (2.0f * standardDeviation * standardDeviation));
//     outKernel[i] = g;
// }
static const float gaussianKernel[BLUR_MAX_SAMPLE_COUNT] = { 1.0, 0.9692332, 0.8824969, 0.7548396, 0.6065307, 0.4578333, 0.3246525, 0.2162652, 0.1353353, 0.07955951, 0.04393693, 0.02279418, 0.011109, 0.005086069, 0.002187491, 0.0008838263 };

#define LOG_Q 3.0

// x = projection[0][0] * 2.0
// y = projection[1][1] * 2.0
// z = unused
// w = unused
float4 _PositionParams;

// x = projection scaled radius
// y = sample count
// z = sample delta X
// w = sample delta Y
float4 _SAO_Params;

// x = bilateral blur offset in X direction (in texel size)
// y = bilateral blur offset in Y direction (in texel size)
// z = bilateral blur depth threshold
// w = bilateral blur sample count
float4 _BilateralBlurParams;

TEXTURE2D_FLOAT(_MipmapDepthTexture);
SAMPLER(sampler_MipmapDepthTexture);
float4 _MipmapDepthTexture_TexelSize;

half2 PackDepth(float normalizedDepth)
{
    float z = clamp(normalizedDepth, 0.0, 1.0);
    float t = floor(256.0 * z);
    half hi = t * (1.0 / 256.0);
    half lo = (256.0 * z) - t;
    return half2(hi, lo);
}

float UnpackDepth(half2 depth)
{
    return depth.x * (256.0 / 257.0) + depth.y * (1.0 / 257.0);
}

float3 ComputeViewSpacePositionFromDepth(float2 uv, float linearDepth)
{
    return float3((0.5 - uv) * _PositionParams.xy * linearDepth, linearDepth);
}

float SampleMipmapDepthLod(float2 uv, float lod = 0.0)
{
    return SAMPLE_TEXTURE2D_LOD(_MipmapDepthTexture, sampler_MipmapDepthTexture, uv, lod).r;
}

// Accurate view-space normal reconstruction
// Based on Yuwen Wu "Accurate Normal Reconstruction"
// (https://atyuwen.github.io/posts/normal-reconstruction)
float3 ComputeViewSpaceNormalAccurate(float2 uv, float depth, float3 positionC, float2 texel)
{
    float2 dx = float2(texel.x, 0.0);
    float2 dy = float2(0.0, texel.y);

    float4 H;
    H.x = SampleMipmapDepthLod(uv - dx);
    H.y = SampleMipmapDepthLod(uv + dx);
    H.z = SampleMipmapDepthLod(uv - dx * 2.0);
    H.w = SampleMipmapDepthLod(uv + dx * 2.0);

    float2 he = abs((2.0 * H.xy - H.zw) - depth);
    float3 pos_l = ComputeViewSpacePositionFromDepth(uv - dx, LinearEyeDepth(H.x, _ZBufferParams));
    float3 pos_r = ComputeViewSpacePositionFromDepth(uv + dx, LinearEyeDepth(H.y, _ZBufferParams));
    float3 dpdx = (he.x < he.y) ? (positionC - pos_l) : (pos_r - positionC);

    float4 V;
    V.x = SampleMipmapDepthLod(uv - dy);
    V.y = SampleMipmapDepthLod(uv + dy);
    V.z = SampleMipmapDepthLod(uv - dy * 2.0);
    V.w = SampleMipmapDepthLod(uv + dy * 2.0);

    float2 ve = abs((2.0 * V.xy - V.zw) - depth);
    float3 pos_d = ComputeViewSpacePositionFromDepth(uv - dy, LinearEyeDepth(V.x, _ZBufferParams));
    float3 pos_u = ComputeViewSpacePositionFromDepth(uv + dy, LinearEyeDepth(V.y, _ZBufferParams));
    float3 dpdy = (ve.x < ve.y) ? (positionC - pos_d) : (pos_u - positionC);

    return normalize(cross(dpdx, dpdy));
}

float3 ComputeViewSpaceNormal(float2 uv, float3 positionC, float2 texel)
{
    float2 dx = float2(texel.x, 0.0);
    float2 dy = float2(0.0, texel.y);

    float2 uvdx = uv + dx;
    float2 uvdy = uv + dy;
    float3 px = ComputeViewSpacePositionFromDepth(uvdx, LinearEyeDepth(SampleMipmapDepthLod(uvdx), _ZBufferParams));
    float3 py = ComputeViewSpacePositionFromDepth(uvdy, LinearEyeDepth(SampleMipmapDepthLod(uvdy), _ZBufferParams));
    float3 dpdx = px - positionC;
    float3 dpdy = py - positionC;

    return normalize(cross(dpdx, dpdy));
}

float2 StartPosition(float noise)
{
    float angle = ((2.0 * PI) * 2.4) * noise;
    return float2(cos(angle), sin(angle));
}

float2x2 TapAngleStep()
{
    float2 t = _SAO_Params.zw;
    return float2x2(t.x, t.y, -t.y, t.x);
}

float3 TapLocationFast(float i, float2 p, float noise)
{
    float radius = (i + noise + 0.5) * 1.0 / (_SAO_Params.y - 0.5);
    return float3(p, radius * radius);
}

void ComputeAmbientOcclusionSAO(inout float occlusion, float i, float ssDiskRadius, float2 uv, float3 origin, float3 normal, float2 tapPosition, float noise)
{
    float3 tap = TapLocationFast(i, tapPosition, noise);

    float ssRadius = max(1.0, tap.z * ssDiskRadius);

    float2 uvSamplePos = uv + float2(ssRadius * tap.xy) * _MipmapDepthTexture_TexelSize.xy;

    float maxLevelIndex = 7.0;
    float lod = clamp(floor(log2(ssRadius) - LOG_Q), 0.0, maxLevelIndex);
    float occlusionDepth = LinearEyeDepth(SampleMipmapDepthLod(uvSamplePos, lod), _ZBufferParams);
    float3 p = ComputeViewSpacePositionFromDepth(uvSamplePos, occlusionDepth);

    float3 v = p - origin;
    float vv = dot(v, v);
    float vn = dot(v, normal);

    const float radius = 0.5;
    float invRadiusSquared = 1.0 / (sq(radius));
    float w = sq(max(0.0, 1.0 - vv * invRadiusSquared));

    const float minHorizonAngleRad = 0.0;
    float minHorizonAngleSineSquared = pow(sin(minHorizonAngleRad), 2.0);
    w *= step(vv * minHorizonAngleSineSquared, vn * vn);

    const float bias = 0.0005;
    const float peak = 0.1 * radius;
    float peak2 = peak * peak;
    float sampleOcclusion = max(0.0, vn + (origin.z * bias)) / (vv + peak2);
    occlusion += w * sampleOcclusion;
}

void ScalableAmbientObscurance(out float obscurance, float2 uv, float3 origin, float3 normal)
{
    float2 fragCoord = uv.xy * _ScreenParams.xy;
    float noise = InterleavedGradientNoise(fragCoord);
    float2 tapPosition = StartPosition(noise);
    float2x2 angleStep = TapAngleStep();

    float ssDiskRadius = -(_SAO_Params.x / origin.z);

    obscurance = 0.0;
    for (float i = 0.0; i < _SAO_Params.y; i += 1.0)
    {
        ComputeAmbientOcclusionSAO(obscurance, i, ssDiskRadius, uv, origin, normal, tapPosition, noise);
        tapPosition = mul(angleStep, tapPosition);
    }

    const float radius = 0.5;
    const float peak = 0.1 * radius;
    const float intensity = (TWO_PI * peak) * 1.0;
    obscurance = sqrt(obscurance * (intensity / _SAO_Params.y));
}

half4 ScalableAOFragment(Varyings input) : SV_TARGET
{
    float2 uv = input.texcoord;

    float depth = SampleMipmapDepthLod(uv);
    float z = LinearEyeDepth(depth, _ZBufferParams);
    float3 origin = ComputeViewSpacePositionFromDepth(uv, z);

    float3 normal = ComputeViewSpaceNormal(uv, origin, _MipmapDepthTexture_TexelSize.xy);

    float occlusion;
    ScalableAmbientObscurance(occlusion, uv, origin, normal);

    const float power = 1.0 * 2.0;
    half aoVisibility = pow(saturate(1.0 - occlusion), power);

    return half4(aoVisibility, PackDepth(origin.z * _ProjectionParams.w), 1.0);
}

float BilateralWeight(in float depth, float sampleDepth)
{
    float diff = (sampleDepth - depth) * _BilateralBlurParams.z;
    return max(0.0, 1.0 - diff * diff);
}

void Tap(inout float sum, inout float totalWeight, float weight, float depth, float2 uv)
{
    // Ambient occlusion sample
    half3 data = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_BlitTexture, uv, 0.0).rgb;

    // Bilateral sample
    float bilateral = weight * BilateralWeight(depth, UnpackDepth(data.gb));
    sum += data.r * bilateral;
    totalWeight += bilateral;
}

half4 BilateralBlurFragment(Varyings input) : SV_TARGET
{
    float2 uv = input.texcoord;
    half3 data = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_BlitTexture, uv, 0.0).rgb;

    // This is the skybox, skip
    UNITY_BRANCH
    if (data.g >= 1.0)
    {
        return half4(data.rgb, 1.0);
    }

    float depth = UnpackDepth(data.gb);
    float totalWeight = gaussianKernel[0];
    float sum = data.r * totalWeight;

    float2 offsetAxis = float2(_BilateralBlurParams.x, 0.0);
    float2 offset = offsetAxis;

    UNITY_UNROLL
    for (int i = 1; i < min((int)_BilateralBlurParams.w, BLUR_MAX_SAMPLE_COUNT); i++)
    {
        float weight = gaussianKernel[i];
        Tap(sum, totalWeight, weight, depth, uv + offset);
        Tap(sum, totalWeight, weight, depth, uv - offset);
        offset += offsetAxis;
    }

    float ao = sum * (1.0 / totalWeight);

    // simple dithering helps a lot (assumes 8 bits target)
    // this is most useful with high quality/large blurs
    ao += ((InterleavedGradientNoise(input.positionCS.xy) - 0.5) / 255.0);

    return half4(ao, data.gb, 1.0);
}

half FinalBilateralBlurFragment(Varyings input) : SV_TARGET
{
    float2 uv = input.texcoord;
    half3 data = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_BlitTexture, uv, 0.0).rgb;

    // This is the skybox, skip
    UNITY_BRANCH
    if (data.g >= 1.0)
    {
        return data.r;
    }

    float depth = UnpackDepth(data.gb);
    float totalWeight = gaussianKernel[0];
    float sum = data.r * totalWeight;

    float2 offsetAxis = float2(0.0, _BilateralBlurParams.y);
    float2 offset = offsetAxis;

    UNITY_UNROLL
    for (int i = 1; i < min((int)_BilateralBlurParams.w, BLUR_MAX_SAMPLE_COUNT); i++)
    {
        float weight = gaussianKernel[i];
        Tap(sum, totalWeight, weight, depth, uv + offset);
        Tap(sum, totalWeight, weight, depth, uv - offset);
        offset += offsetAxis;
    }

    float ao = sum * (1.0 / totalWeight);

    // simple dithering helps a lot (assumes 8 bits target)
    // this is most useful with high quality/large blurs
    ao += ((InterleavedGradientNoise(input.positionCS.xy) - 0.5) / 255.0);

    return ao;
}

#endif
