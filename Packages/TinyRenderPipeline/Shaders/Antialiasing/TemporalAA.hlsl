#ifndef TINY_RP_TEMPORAL_AA_INCLUDED
#define TINY_RP_TEMPORAL_AA_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"
#include "Packages/com.zwcmc.tiny-rp/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.zwcmc.tiny-rp/ShaderLibrary/CommonMath.hlsl"

TEXTURE2D(_TaaHistoryTexture);
float4 _TaaHistoryTexture_TexelSize;

float4x4 _HistoryReprojection;

// Gaussian fit of a 3.3-wide Blackman-Harris window
float _TaaFilterWeights[9];

// x = blend alpha
// y = stDev scale
// z = is first frame
// w = unused
float4 _TaaFrameInfo;

static const float2 filterOffsets[9] =
{
    float2(-1.0, -1.0), float2(0.0, -1.0), float2(1.0, -1.0),
    float2(-1.0, 0.0),  float2(0.0, 0.0),  float2(1.0, 0.0),
    float2(-1.0, 1.0),  float2(0.0, 1.0),  float2(1.0, 1.0)
};

// Samples a texture with Catmull-Rom filtering, using 9 texture fetches instead of 16.
//      https://therealmjp.github.io/
// Some optimizations from here:
//      http://vec3.ca/bicubic-filtering-in-fewer-taps/ for more details
// Optimized to 5 taps by removing the corner samples
// And modified for mediump support
half3 SampleTextureBicubic5Tap(TEXTURE2D(tex), float2 uv, float4 texelSize)
{
    // We're going to sample a a 4x4 grid of texels surrounding the target UV coordinate.
    // We'll do this by rounding down the sample location to get the exact center of our "starting"
    // texel. The starting texel will be at location [1, 1] in the grid, where [0, 0] is the
    // top left corner.

    float2 samplePos = uv * texelSize.zw;
    float2 texPos1 = floor(samplePos - 0.5) + 0.5;

    // Compute the fractional offset from our starting texel to our original sample location,
    // which we'll feed into the Catmull-Rom spline function to get our filter weights.

    float2 f = samplePos - texPos1;
    float2 f2 = f * f;
    float2 f3 = f * f2;

    // Compute the Catmull-Rom weights using the fractional offset that we calculated earlier.
    // These equations are pre-expanded based on our knowledge of where the texels will be located,
    // which lets us avoid having to evaluate a piece-wise function.
    float2 w0 = f2 - 0.5 * (f3 + f);
    float2 w1 = 1.5 * f3 - 2.5 * f2 + 1.0;
    float2 w3 = 0.5 * (f3 - f2);
    float2 w2 = 1.0 - w0 - w1 - w3;

    // Work out weighting factors and sampling offsets that will let us use bilinear filtering to
    // simultaneously evaluate the middle 2 samples from the 4x4 grid.
    float2 w12 = w1 + w2;

    // Compute the final UV coordinates we'll use for sampling the texture
    float2 texPos0 = texPos1 - float2(1.0, 1.0);
    float2 texPos3 = texPos1 + float2(2.0, 2.0);
    float2 texPos12 = texPos1 + w2 / w12;

    texPos0  *= texelSize.xy;
    texPos3  *= texelSize.xy;
    texPos12 *= texelSize.xy;

    float k0 = w12.x * w0.y;
    float k1 = w0.x  * w12.y;
    float k2 = w12.x * w12.y;
    float k3 = w3.x  * w12.y;
    float k4 = w12.x * w3.y;

    half3 s[5];
    s[0] = SAMPLE_TEXTURE2D_LOD(tex, sampler_LinearClamp, float2(texPos12.x, texPos0.y),  0.0).rgb;
    s[1] = SAMPLE_TEXTURE2D_LOD(tex, sampler_LinearClamp, float2(texPos0.x,  texPos12.y), 0.0).rgb;
    s[2] = SAMPLE_TEXTURE2D_LOD(tex, sampler_LinearClamp, float2(texPos12.x, texPos12.y), 0.0).rgb;
    s[3] = SAMPLE_TEXTURE2D_LOD(tex, sampler_LinearClamp, float2(texPos3.x,  texPos12.y), 0.0).rgb;
    s[4] = SAMPLE_TEXTURE2D_LOD(tex, sampler_LinearClamp, float2(texPos12.x, texPos3.y),  0.0).rgb;

    half3 result =  k0 * s[0]
                  + k1 * s[1]
                  + k2 * s[2]
                  + k3 * s[3]
                  + k4 * s[4];

    result *= rcp(k0 + k1 + k2 + k3 + k4);

    // we could end-up with negative values
    result = max(half3(0.0, 0.0, 0.0), result);

    return result;
}

half LumaYCoCg(half3 c)
{
    return c.x;
}

half3 ToneMapReinhard(half3 c)
{
    return c * rcp(1.0 + max3(c));
}

half3 UnToneMapReinhard(half3 c)
{
    return c * rcp(max(1.0 / HALF_MAX, 1.0 - max3(c)));
}

half3 FetchOffset(TEXTURE2D(tex), float2 coords, float2 offset, float4 texelSize)
{
    float2 uv = coords + offset * texelSize.xy;
    return SAMPLE_TEXTURE2D_LOD(tex, sampler_LinearClamp, uv, 0.0).rgb;
}

// Here the ray referenced goes from history to (filtered) center color
float DistToAABB(half3 color, half3 history, half3 minimum, half3 maximum)
{
    half3 center = 0.5 * (maximum + minimum);
    half3 extents = 0.5 * (maximum - minimum);

    half3 rayDir = color - history;
    half3 rayPos = history - center;

    half3 invDir = rcp(rayDir);
    half3 t0 = (extents - rayPos)  * invDir;
    half3 t1 = -(extents + rayPos) * invDir;

    float AABBIntersection = max(max(min(t0.x, t1.x), min(t0.y, t1.y)), min(t0.z, t1.z));
    return saturate(AABBIntersection);
}

half3 ClipAABB(half3 aabbMin, half3 aabbMax, half3 history, half3 c)
{
#if 1
    // Note: only clips towards aabb center (but fast!)
    float3 center = 0.5 * (aabbMax + aabbMin);
    float3 extents = 0.5 * (aabbMax - aabbMin);

    // This is actually `distance`, however the keyword is reserved
    float3 offset = history - center;

    float3 ts = abs(offset / extents);
    float t = Max3(ts.x, ts.y, ts.z);

    return t > 1 ? center + offset / t : history;
#else
    float historyBlend = DistToAABB(c, history, aabbMin, aabbMax);
    return lerp(history, c, historyBlend);
#endif
}

half4 TemporalAAFragment(Varyings input) : SV_Target0
{
    float4 uv = input.uv.xyxy;

    // 如果是第一帧, 则直接输出采样结果
    if (_TaaFrameInfo.z > 0.5)
    {
        half3 filtered = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, uv.xy, 0.0).rgb;
        return half4(filtered, 1.0);
    }

    // 当前像素的中心位置
    float2 ph = (floor(uv.zw * _BlitTexture_TexelSize.zw) + 0.5) * _BlitTexture_TexelSize.xy;

    // 采样深度, 并重投影到上一帧的位置
    float deviceDepth = SAMPLE_TEXTURE2D_LOD(_CameraDepthTexture, sampler_PointClamp, ph, 0.0).r;
    float4 q = mul(_HistoryReprojection, float4(ph, deviceDepth, 1.0));
    uv.zw = (q.xy * (1.0 / q.w)) * 0.5 + 0.5;

    // 采样历史像素数据
    half3 history = SampleTextureBicubic5Tap(_TaaHistoryTexture, uv.zw, _TaaHistoryTexture_TexelSize).rgb;
    history.rgb = RGBToYCoCg(history.rgb);

    // 考虑当前像素相邻的 3x3 的像素, 采样当前帧的像素数据, 从 RGB 颜色空间转换到 YCoCg 颜色空间
    float2 p = uv.xy;
    half3 filtered = 0.0;
    half3 filterSamples[9];

    UNITY_UNROLL
    for (uint i = 0; i < 9; ++i)
    {
        filterSamples[i] = RGBToYCoCg(FetchOffset(_BlitTexture, p, filterOffsets[i], _BlitTexture_TexelSize));
        filtered += filterSamples[i] * _TaaFilterWeights[i];
    }

    // 构建当前帧 3x3 像素数据的 AABB , 用以对历史像素数据进行修正
    // 计算均值和标准差
    float3 m1 = 0.0; // conversion to highp
    float3 m2 = 0.0;

    UNITY_UNROLL
    for (uint j = 0; j < 9; ++j)
    {
        float3 c = filterSamples[j]; // conversion to highp
        m1 += c;
        m2 += c * c;
    }
    float invSamples = rcp(9.0);
    float3 mean = m1 * invSamples;
    float3 stdDev = sqrt(abs(m2 * invSamples - mean * mean));

    float stDevMultiplier = _TaaFrameInfo.y;
    half3 boxmin = mean - stDevMultiplier * stdDev;
    half3 boxmax = mean + stDevMultiplier * stdDev;

    boxmin = min(boxmin, filtered);
    boxmax = max(boxmax, filtered);

    // 验证历史像素数据
    // TODO: investigate ssr is jittering when ClipAABB
    history = ClipAABB(boxmin, boxmax, history, filtered);

    // 混合权重 alpha
    half blendAlpha = _TaaFrameInfo.x;

    // 转换到 RGB 颜色空间
    filtered = YCoCgToRGB(filtered);
    history = YCoCgToRGB(history);

    // 色调映射
    filtered = ToneMapReinhard(filtered);
    history = ToneMapReinhard(history);

    // 混合历史像素数据与当前帧的像素数据
    half3 result = lerp(history, filtered, blendAlpha);

    // 逆向色调映射
    result = UnToneMapReinhard(result);

    return half4(clamp(result, 0.0, HALF_MAX), 1.0);
}

half4 CopyHistoryFragment(Varyings input) : SV_Target0
{
    float2 uv = input.uv;
    return SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_BlitTexture, uv, 0.0);
}

#endif
