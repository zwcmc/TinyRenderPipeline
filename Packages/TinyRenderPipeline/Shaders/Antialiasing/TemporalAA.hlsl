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
// y = variance gamma
// z = is first frame
// w = unused
float4 _TaaFrameInfo;

#define EPSILON 0.0001

// Samples a texture with Catmull-Rom filtering, using 9 texture fetches instead of 16.
//      https://therealmjp.github.io/
// Some optimizations from here:
//      http://vec3.ca/bicubic-filtering-in-fewer-taps/ for more details
// Optimized to 5 taps by removing the corner samples
// And modified for mediump support
half4 SampleTextureBicubic5Tap(TEXTURE2D(tex), float2 uv, float4 texelSize)
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

    half4 s[5];
    s[0] = SAMPLE_TEXTURE2D_LOD(tex, sampler_LinearClamp, float2(texPos12.x, texPos0.y),  0.0);
    s[1] = SAMPLE_TEXTURE2D_LOD(tex, sampler_LinearClamp, float2(texPos0.x,  texPos12.y), 0.0);
    s[2] = SAMPLE_TEXTURE2D_LOD(tex, sampler_LinearClamp, float2(texPos12.x, texPos12.y), 0.0);
    s[3] = SAMPLE_TEXTURE2D_LOD(tex, sampler_LinearClamp, float2(texPos3.x,  texPos12.y), 0.0);
    s[4] = SAMPLE_TEXTURE2D_LOD(tex, sampler_LinearClamp, float2(texPos12.x, texPos3.y),  0.0);

    half4 result =  k0 * s[0]
                  + k1 * s[1]
                  + k2 * s[2]
                  + k3 * s[3]
                  + k4 * s[4];

    result *= rcp(k0 + k1 + k2 + k3 + k4);

    // we could end-up with negative values
    result = max(half4(0.0, 0.0, 0.0, 0.0), result);

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

half4 FetchOffset(TEXTURE2D(tex), float2 coords, float2 offset, float4 texelSize)
{
    float2 uv = coords + offset * texelSize.xy;
    return SAMPLE_TEXTURE2D_LOD(tex, sampler_LinearClamp, uv, 0.0);
}

half4 ClipToBox(half3 boxmin, half3 boxmax, half4 c, half4 h)
{
    half4 r = c - h;
    half3 ir = 1.0 / (EPSILON + r.rgb);
    half3 rmax = (boxmax - h.rgb) * ir;
    half3 rmin = (boxmin - h.rgb) * ir;
    half3 imin = min(rmax, rmin);
    return h + r * saturate(max3(imin));
}

half4 TemporalAAFragment(Varyings input) : SV_Target0
{
    float4 uv = input.uv.xyxy;

    // 如果是第一帧, 则直接输出采样结果
    if (_TaaFrameInfo.z > 0.5)
    {
        half4 filtered = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, uv.xy, 0.0);
        return filtered;
    }

    // 当前像素的中心位置
    float2 ph = (floor(uv.zw * _BlitTexture_TexelSize.zw) + 0.5) * _BlitTexture_TexelSize.xy;

    // 采样深度, 并重投影到上一帧的位置
    float depth = SAMPLE_TEXTURE2D_LOD(_CameraDepthTexture, sampler_PointClamp, ph, 0.0).r;
    float4 q = mul(_HistoryReprojection, float4(ph, depth, 1.0));
    uv.zw = (q.xy * (1.0 / q.w)) * 0.5 + 0.5;

    // 采样历史像素数据
    half4 history = SampleTextureBicubic5Tap(_TaaHistoryTexture, uv.zw, _TaaHistoryTexture_TexelSize);
    history.rgb = RGBToYCoCg(history.rgb);

    // 采样当前帧像素数据
    float2 p = uv.xy;
    half4 filtered = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, p, 0.0);

    // 考虑当前像素相邻的 3x3 的像素, 采样当前帧的像素数据, 从 RGB 颜色空间转换到 YCoCg 颜色空间
    half3 s[9];
    s[0] = RGBToYCoCg(FetchOffset(_BlitTexture, p, float2(-1.0, -1.0), _BlitTexture_TexelSize).rgb);
    s[1] = RGBToYCoCg(FetchOffset(_BlitTexture, p, float2(0.0, -1.0), _BlitTexture_TexelSize).rgb);
    s[2] = RGBToYCoCg(FetchOffset(_BlitTexture, p, float2(1.0, -1.0), _BlitTexture_TexelSize).rgb);
    s[3] = RGBToYCoCg(FetchOffset(_BlitTexture, p, float2(-1.0, 0.0), _BlitTexture_TexelSize).rgb);
    s[4] = RGBToYCoCg(filtered.rgb);
    s[5] = RGBToYCoCg(FetchOffset(_BlitTexture, p, float2(1.0, 0.0), _BlitTexture_TexelSize).rgb);
    s[6] = RGBToYCoCg(FetchOffset(_BlitTexture, p, float2(-1.0, 1.0), _BlitTexture_TexelSize).rgb);
    s[7] = RGBToYCoCg(FetchOffset(_BlitTexture, p, float2(0.0, 1.0), _BlitTexture_TexelSize).rgb);
    s[8] = RGBToYCoCg(FetchOffset(_BlitTexture, p, float2(1.0, 1.0), _BlitTexture_TexelSize).rgb);

    // 使用 3.3 宽 Blackman-Harris 窗口的高斯拟合来对当前帧 3x3 的像素区域的像素数据进行滤波
    filtered = half4(0, 0, 0, filtered.a);
    UNITY_UNROLL
    for(int i = 0; i < 9; ++i)
    {
        float w = _TaaFilterWeights[i];
        filtered.rgb += s[i] * w;
    }

    // 构建当前帧 3x3 像素数据的 AABB , 用以对历史像素数据进行修正
    // 这里是通过 最小值和最大值 以及 均值和标准差 混合的方式来构建 AABB , 算法来自 filament
    half3 boxmin = min(s[4], min(min(s[1], s[3]), min(s[5], s[7])));
    half3 boxmax = max(s[4], max(max(s[1], s[3]), max(s[5], s[7])));
    half3 box9min = min(boxmin, min(min(s[0], s[2]), min(s[6], s[8])));
    half3 box9max = max(boxmax, max(max(s[0], s[2]), max(s[6], s[8])));
    // round the corners of the 3x3 box
    boxmin = (boxmin + box9min) * 0.5;
    boxmax = (boxmax + box9max) * 0.5;

    // 计算均值和标准差
    // "An Excursion in Temporal Supersampling" by Marco Salvi
    float3 m0 = s[4]; // conversion to highp
    float3 m1 = m0 * m0;
    // we use only 5 samples instead of all 9
    UNITY_UNROLL
    for (int j = 1; j < 9; j+=2)
    {
        float3 c = s[j]; // conversion to highp
        m0 += c;
        m1 += c * c;
    }
    float3 a0 = m0 * (1.0 / 5.0);
    float3 a1 = m1 * (1.0 / 5.0);
    float3 sigma = sqrt(abs(a1 - a0 * a0));

    // intersect both bounding boxes
    boxmin = max(boxmin, a0 - _TaaFrameInfo.y * sigma);
    boxmax = min(boxmax, a0 + _TaaFrameInfo.y * sigma);

    // 验证历史像素数据
    history = ClipToBox(boxmin, boxmax, filtered, history);

    // 混合权重 alpha
    half alpha = _TaaFrameInfo.x;

    // 消除闪烁
    // [Lottes] prevents flickering by modulating the blend weight by the difference in luma
    // half lumaColor = LumaYCoCg(filtered.rgb);
    // half lumaHistory = LumaYCoCg(history.rgb);
    // half diff = 1.0 - abs(lumaColor - lumaHistory) / (0.001 + max(lumaColor, lumaHistory));
    // alpha *= diff * diff;

    // 转换到 RGB 颜色空间
    filtered.rgb = YCoCgToRGB(filtered.rgb);
    history.rgb = YCoCgToRGB(history.rgb);

    // 色调映射
    filtered.rgb = ToneMapReinhard(filtered.rgb);
    history.rgb = ToneMapReinhard(history.rgb);

    // 混合历史像素数据与当前帧的像素数据
    half4 result = lerp(history, filtered, alpha);

    // 逆向色调映射
    result.rgb = UnToneMapReinhard(result.rgb);

    return result;
}

half4 CopyHistoryFragment(Varyings input) : SV_Target0
{
    float2 uv = input.uv;
    return SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_BlitTexture, uv, 0.0);
}

#endif
