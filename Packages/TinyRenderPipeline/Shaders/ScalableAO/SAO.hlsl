#ifndef TINY_RP_SAO_INCLUDED
#define TINY_RP_SAO_INCLUDED

#include "Packages/com.zwcmc.tiny-rp/ShaderLibrary/CommonMath.hlsl"

#define BLUR_MAX_SAMPLE_COUNT 8
// Generated (in C#) with:
// const float standardDeviation = 4.0f;
// const int gaussianSampleCount = 8;
// float[] outKernel = new float[gaussianSampleCount];
// for (int i = 0; i < gaussianSampleCount; i++)
// {
//     float x = (float)i;
//     float g = Mathf.Exp(-(x * x) / (2.0f * standardDeviation * standardDeviation));
//     Debug.Log(g);
//     outKernel[i] = g;
// }
static const float gaussianKernel[BLUR_MAX_SAMPLE_COUNT] = { 1.0, 0.9692332, 0.8824969, 0.7548396, 0.6065307, 0.4578333, 0.3246525, 0.2162652 };

#define LOG_Q 3.0

// x = projection[0][0] * 2.0
// y = projection[1][1] * 2.0
// z = projection scaled radius
// w = mipmap count of the mipmap depth texture
float4 _PositionParams;

// x = radius
// y = sample count
// z = sample delta X
// w = sample delta Y
float4 _SaoParams;

// x = bilateral blur offset in X direction (in texel size)
// y = bilateral blur offset in Y direction (in texel size)
// z = bilateral blur depth threshold
// w = bilateral blur sample count
float4 _BilateralBlurParams;

// step tap radius
float _StepTapRadius;

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

float3 ReconstructViewSpacePositionFromDepth(float2 uv, float linearDepth)
{
    return float3((uv - 0.5) * _PositionParams.xy * linearDepth, linearDepth);
}

float SampleMipmapDepthLod(float2 uv, float lod = 0.0)
{
    return SAMPLE_TEXTURE2D_LOD(_MipmapDepthTexture, sampler_MipmapDepthTexture, uv, lod).r;
}

// // Accurate view-space normal reconstruction
// // Based on Yuwen Wu "Accurate Normal Reconstruction"
// // (https://atyuwen.github.io/posts/normal-reconstruction)
// float3 ComputeViewSpaceNormalAccurate(float2 uv, float depth, float3 positionC, float2 texel)
// {
//     float2 dx = float2(texel.x, 0.0);
//     float2 dy = float2(0.0, texel.y);
//
//     float4 H;
//     H.x = SampleMipmapDepthLod(uv - dx);
//     H.y = SampleMipmapDepthLod(uv + dx);
//     H.z = SampleMipmapDepthLod(uv - dx * 2.0);
//     H.w = SampleMipmapDepthLod(uv + dx * 2.0);
//
//     float2 he = abs((2.0 * H.xy - H.zw) - depth);
//     float3 pos_l = ReconstructViewSpacePositionFromDepth(uv - dx, H.x);
//     float3 pos_r = ReconstructViewSpacePositionFromDepth(uv + dx, H.y);
//     float3 dpdx = (he.x < he.y) ? (positionC - pos_l) : (pos_r - positionC);
//
//     float4 V;
//     V.x = SampleMipmapDepthLod(uv - dy);
//     V.y = SampleMipmapDepthLod(uv + dy);
//     V.z = SampleMipmapDepthLod(uv - dy * 2.0);
//     V.w = SampleMipmapDepthLod(uv + dy * 2.0);
//
//     float2 ve = abs((2.0 * V.xy - V.zw) - depth);
//     float3 pos_d = ReconstructViewSpacePositionFromDepth(uv - dy, V.x);
//     float3 pos_u = ReconstructViewSpacePositionFromDepth(uv + dy, V.y);
//     float3 dpdy = (ve.x < ve.y) ? (positionC - pos_d) : (pos_u - positionC);
//
//     return normalize(cross(dpdx, dpdy));
// }

// 重建观察空间中的法线向量
float3 ReconstructViewSpaceNormal(float2 uv, float3 C, float2 texelSize)
{
    float2 dx = float2(texelSize.x, 0.0);
    float2 dy = float2(0.0, texelSize.y);

    float2 uvdx = uv + dx;
    float2 uvdy = uv + dy;
    float3 px = ReconstructViewSpacePositionFromDepth(uvdx, SampleMipmapDepthLod(uvdx));
    float3 py = ReconstructViewSpacePositionFromDepth(uvdy, SampleMipmapDepthLod(uvdy));

    float3 dpdx = px - C;
    float3 dpdy = py - C;
    return normalize(cross(dpdx, dpdy));
}

// 随机圆盘上的初始采样点
float2 StartPosition(float jitter)
{
    float angle = (TWO_PI * 2.4) * jitter;
    return float2(cos(angle), sin(angle));
}

// 通过采样旋转角度的 cos 和 sin 值构建一个 2x2 的旋转矩阵
float2x2 TapAngleStepMatrix()
{
    float2 t = _SaoParams.zw;
    return float2x2(t.x, t.y, -t.y, t.x);
}

// 螺旋盘采样每次采样点距离中心的距离, r * r 是为了让采样点在靠近中心的区域分布更墨迹
float TapRadius(float tapIndex, float jitter)
{
    float r = (tapIndex + jitter + 0.5) * _StepTapRadius;
    return r * r;
}

void ComputeAmbientOcclusionSAO(inout float sum, float ssDiskRadius, float2 uv, float3 origin, float3 normal, float2 tapPosition, float ssR)
{
    // 最小是 1 个像素大小
    float ssRadius = max(1.0, ssR * ssDiskRadius);

    // 计算采样坐标
    float2 uvSamplePos = uv + float2(ssRadius * tapPosition) * _MipmapDepthTexture_TexelSize.xy;

    // 根据像素大小计算采样的 mipmap 层级
    float level = clamp(floor(log2(ssRadius) - LOG_Q), 0.0, _PositionParams.w - 1.0);

    // 采样线性深度
    float occlusionDepth = SampleMipmapDepthLod(uvSamplePos, level);

    // 计算采样点在观察空间中的坐标
    float3 p = ReconstructViewSpacePositionFromDepth(uvSamplePos, occlusionDepth);

    // 观察空间中采样点到中心点的向量 v
    float3 v = p - origin;

    // 采样点到中心的距离平方
    float vv = dot(v, v);

    // 法线向量 normal 是归一化的, 所以 vv 是向量 v 的长度投影到法线向量上的长度
    // 可以理解为是采样点与中心点在法线方向上深度的变化
    float vn = dot(v, normal);

    // 世界空间最大采样半径（1m）的平方
    float radius2 = sq(_SaoParams.x);

    // 采样点超出采样范围, 则丢弃掉, 也就是限制到 0.0
    float f = max(radius2 - vv, 0.0);

    // 计算 AO
    const float epsilon = 0.01;
    const float bias = 0.008;

    // 计算 AO 贡献, 深度的变化越大, 对 AO 的贡献就越大, 而除以 vv 是保证采样点距离中心点越远, 则对 AO 的贡献越小
    sum += f * f * f * max(0.0, (vn - bias) / (vv + epsilon));
}

void ScalableAmbientObscurance(out float sumObscurance, float2 uv, float3 origin, float3 normal)
{
    float2 fragCoord = uv.xy * _ScreenParams.xy;
    // 随机抖动
    float jitter = InterleavedGradientNoise(fragCoord);

    // 随机初始化采样点
    float2 tapPosition = StartPosition(jitter);

    // 每次采样旋转角度的矩阵
    float2x2 angleStepMatrix = TapAngleStepMatrix();

    // 根据线性深度计算采样的圆盘半径大小(以屏幕像素为单位)
    float ssDiskRadius = -(_PositionParams.z / origin.z);

    sumObscurance = 0.0;
    for (float i = 0.0; i < _SaoParams.y; i += 1.0)
    {
        // 每次采样的半径 r
        float ssR = TapRadius(i, jitter);
        ComputeAmbientOcclusionSAO(sumObscurance, ssDiskRadius, uv, origin, normal, tapPosition, ssR);
        tapPosition = mul(angleStepMatrix, tapPosition);
    }
}

half4 ScalableAOFragment(Varyings input) : SV_TARGET
{
    float2 uv = input.texcoord;

    float z = SampleMipmapDepthLod(uv);
    float3 C = ReconstructViewSpacePositionFromDepth(uv, z);
    float3 n_C = ReconstructViewSpaceNormal(uv, C, _MipmapDepthTexture_TexelSize.xy);

    float sumOcclusion;
    ScalableAmbientObscurance(sumOcclusion, uv, C, n_C);

    // ao 的强度
    const float aoIntensity = 1.0;
    float intensityDivR6 = aoIntensity / (pow5(_SaoParams.x) * _SaoParams.x);
    half aoVisibility = max(0.0, 1.0 - sumOcclusion * intensityDivR6 * (3.0 / _SaoParams.y));

    return half4(aoVisibility, PackDepth(C.z * _ProjectionParams.w), 1.0);
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

    return ao;
}

#endif
