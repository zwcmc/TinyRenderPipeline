#ifndef TINY_RP_PCSS_INCLUDED
#define TINY_RP_PCSS_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

// Fibonacci Spiral Disk Sampling Pattern
// https://people.irisa.fr/Ricardo.Marques/articles/2013/SF_CGF.pdf
//
// Normalized direction vector portion of fibonacci spiral can be baked into a LUT, regardless of sampleCount.
// This allows us to treat the directions as a progressive sequence, using any sampleCount in range [0, n <= LUT_LENGTH]
// the radius portion of spiral construction is coupled to sample count, but is fairly cheap to compute at runtime per sample.
// Generated (in javascript) with:
// var res = "";
// for (var i = 0; i < 64; ++i)
// {
//     var a = Math.PI * (3.0 - Math.sqrt(5.0));
//     var b = a / (2.0 * Math.PI);
//     var c = i * b;
//     var theta = (c - Math.floor(c)) * 2.0 * Math.PI;
//     res += "float2 (" + Math.cos(theta) + ", " + Math.sin(theta) + "),\n";
// }
#define DISK_SAMPLE_COUNT 64
static const float2 fibonacciSpiralDirection[DISK_SAMPLE_COUNT] =
{
    float2 (1, 0),
    float2 (-0.7373688780783197, 0.6754902942615238),
    float2 (0.08742572471695988, -0.9961710408648278),
    float2 (0.6084388609788625, 0.793600751291696),
    float2 (-0.9847134853154288, -0.174181950379311),
    float2 (0.8437552948123969, -0.5367280526263233),
    float2 (-0.25960430490148884, 0.9657150743757782),
    float2 (-0.46090702471337114, -0.8874484292452536),
    float2 (0.9393212963241182, 0.3430386308741014),
    float2 (-0.924345556137805, 0.3815564084749356),
    float2 (0.423845995047909, -0.9057342725556143),
    float2 (0.29928386444487326, 0.9541641203078969),
    float2 (-0.8652112097532296, -0.501407581232427),
    float2 (0.9766757736281757, -0.21471942904125949),
    float2 (-0.5751294291397363, 0.8180624302199686),
    float2 (-0.12851068979899202, -0.9917081236973847),
    float2 (0.764648995456044, 0.6444469828838233),
    float2 (-0.9991460540072823, 0.04131782619737919),
    float2 (0.7088294143034162, -0.7053799411794157),
    float2 (-0.04619144594036213, 0.9989326054954552),
    float2 (-0.6407091449636957, -0.7677836880006569),
    float2 (0.9910694127331615, 0.1333469877603031),
    float2 (-0.8208583369658855, 0.5711318504807807),
    float2 (0.21948136924637865, -0.9756166914079191),
    float2 (0.4971808749652937, 0.8676469198750981),
    float2 (-0.952692777196691, -0.30393498034490235),
    float2 (0.9077911335843911, -0.4194225289437443),
    float2 (-0.38606108220444624, 0.9224732195609431),
    float2 (-0.338452279474802, -0.9409835569861519),
    float2 (0.8851894374032159, 0.4652307598491077),
    float2 (-0.9669700052147743, 0.25489019011123065),
    float2 (0.5408377383579945, -0.8411269468800827),
    float2 (0.16937617250387435, 0.9855514761735877),
    float2 (-0.7906231749427578, -0.6123030256690173),
    float2 (0.9965856744766464, -0.08256508601054027),
    float2 (-0.6790793464527829, 0.7340648753490806),
    float2 (0.0048782771634473775, -0.9999881011351668),
    float2 (0.6718851669348499, 0.7406553331023337),
    float2 (-0.9957327006438772, -0.09228428288961682),
    float2 (0.7965594417444921, -0.6045602168251754),
    float2 (-0.17898358311978044, 0.9838520605119474),
    float2 (-0.5326055939855515, -0.8463635632843003),
    float2 (0.9644371617105072, 0.26431224169867934),
    float2 (-0.8896863018294744, 0.4565723210368687),
    float2 (0.34761681873279826, -0.9376366819478048),
    float2 (0.3770426545691533, 0.9261958953890079),
    float2 (-0.9036558571074695, -0.4282593745796637),
    float2 (0.9556127564793071, -0.2946256262683552),
    float2 (-0.50562235513749, 0.8627549095688868),
    float2 (-0.2099523790012021, -0.9777116131824024),
    float2 (0.8152470554454873, 0.5791133210240138),
    float2 (-0.9923232342597708, 0.12367133357503751),
    float2 (0.6481694844288681, -0.7614961060013474),
    float2 (0.036443223183926, 0.9993357251114194),
    float2 (-0.7019136816142636, -0.7122620188966349),
    float2 (0.998695384655528, 0.05106396643179117),
    float2 (-0.7709001090366207, 0.6369560596205411),
    float2 (0.13818011236605823, -0.9904071165669719),
    float2 (0.5671206801804437, 0.8236347091470047),
    float2 (-0.9745343917253847, -0.22423808629319533),
    float2 (0.8700619819701214, -0.49294233692210304),
    float2 (-0.30857886328244405, 0.9511987621603146),
    float2 (-0.4149890815356195, -0.9098263912451776),
    float2 (0.9205789302157817, 0.3905565685566777)
};

#define FIND_BLOCKER_SAMPLE_COUNT 24
#define PCSS_SAMPLE_COUNT 16

// 基于斐波那契螺旋盘生成在中心区域分布更密集的随机采样坐标点，用于 Blocker 的搜索
float2 ComputeFibonacciSpiralDiskSampleClumped(const in int sampleIndex, const in float sampleCountInverse, out float sampleDistNorm)
{
    // 在这里不使用 sampleBias 是因为中心点 (0, 0) 的采样结果对于 Blocker 的搜索很重要
    sampleDistNorm = (float)sampleIndex * sampleCountInverse;

    // pow(r, 3.0) ， 越靠近中心的区域，采样点分布更密集，更有利于 Blocker 的搜索
    sampleDistNorm = sampleDistNorm * sampleDistNorm * sampleDistNorm;

    // 半径乘以 \cos\phi 和 \sin\phi 得到采样坐标点
    return fibonacciSpiralDirection[sampleIndex] * sampleDistNorm;
}

// 基于斐波那契螺旋盘生成随机采样坐标点
float2 ComputeFibonacciSpiralDiskSampleUniform(const in int sampleIndex, const in float sampleCountInverse, const in float sampleBias, out float sampleDistNorm)
{
    // sampleBias 是为了防止当 sampleIndex = 0 时，生成出的采样坐标点是 (0, 0)，采样坐标点为 (0, 0) 时，最终采样不会受到 Jitter 的影响，并且会造成明显的边缘 artifact
    sampleDistNorm = (float)sampleIndex * sampleCountInverse + sampleBias;

    // 圆盘的面积与半径的平方成正比，这意味着半径的增量在距离中心较远的地方会覆盖更大的面积。每个均匀半径增量将包含相同数量的点，这会导致靠近中心区域较小面积的面盘上的采样点更密集
    // 通过使用 sqrt(r) 使采样点在整个圆盘上均匀分布
    sampleDistNorm = sqrt(sampleDistNorm);

    // 半径乘以 \cos\phi 和 \sin\phi 得到采样坐标点
    return fibonacciSpiralDirection[sampleIndex] * sampleDistNorm;
}

bool BlockerSearch(inout float averageBlocker, float filterSize, float4 shadowCoord, float2 shadowMapInAtlasScale, float2 shadowMapInAtlasOffset, float2 sampleJitter, Texture2D shadowMap, SamplerState pointSampler, int sampleCount, float radial2DepthScale, float minFilterRadius, float minFilterRadial2DepthScale)
{

#if UNITY_REVERSED_Z
    #define Z_OFFSET_DIRECTION 1
#else
    #define Z_OFFSET_DIRECTION (-1)
#endif

    float sampleCountInverse = rcp((float)sampleCount);

    // 当前 Cascade 在 Shadow map atlas 上的 uv 范围
    float2 minCoord = shadowMapInAtlasOffset;
    float2 maxCoord = minCoord + shadowMapInAtlasScale;

    averageBlocker = 0.0;
    float sum = 0.0;
    float totalSamples = 0.0;
    for (int i = 0; i < sampleCount && i < DISK_SAMPLE_COUNT; ++i)
    {
        // 计算 Blocker 搜索的随机采样点
        float sampleDistNorm;
        float2 offset = ComputeFibonacciSpiralDiskSampleClumped(i, sampleCountInverse, sampleDistNorm);

        // 应用随机抖动
        offset = float2(offset.x * sampleJitter.y + offset.y * sampleJitter.x,
                        offset.x * -sampleJitter.x + offset.y * sampleJitter.y);

        // 计算阴影贴图上的偏移
        offset *= filterSize;
        // 转换到 Cascade 上偏移
        offset *= shadowMapInAtlasScale;

        float2 sampleCoord = shadowCoord.xy + offset;

        // 将着色点的深度进行偏移, 以符合使用直角径来定义方向光的设定 , 这对于消除自遮挡很重要
        float radialOffset = filterSize * sampleDistNorm;
        float zoffset = radialOffset * (radialOffset < minFilterRadius ? minFilterRadial2DepthScale : radial2DepthScale);
        float coordz = shadowCoord.z + (Z_OFFSET_DIRECTION) * zoffset;

        float blocker = SAMPLE_TEXTURE2D_LOD(shadowMap, pointSampler, sampleCoord, 0.0).x;
        // 判断是否超过 Cascade 边界 , 并且深度要比着色点的深度小, 这种情况才算作 blocker
        if (!(any(sampleCoord < minCoord) || any(sampleCoord > maxCoord)) && COMPARE_DEVICE_DEPTH_CLOSER(blocker, coordz))
        {
            sum += blocker;
            totalSamples += 1.0;
        }
    }

    if (totalSamples > 0.0)
    {
        averageBlocker = sum / totalSamples;
        return true;
    }
    else
        return false;
}

float PCF_Filter(TEXTURE2D_SHADOW_PARAM(shadowMap, sampler_shadowMap), float4 shadowCoord, float filterSize, float2 shadowMapInAtlasScale, float2 shadowMapInAtlasOffset, float2 sampleJitter, int sampleCount, float radial2DepthScale, float maxPCSSOffset, float samplingFilterSize)
{

#if UNITY_REVERSED_Z
    #define Z_OFFSET_DIRECTION 1
#else
    #define Z_OFFSET_DIRECTION (-1)
#endif

    float2 minCoord = shadowMapInAtlasOffset;
    float2 maxCoord = shadowMapInAtlasOffset + shadowMapInAtlasScale;

    float sampleCountInverse = rcp((float)sampleCount);
    float sampleCountBias = 0.5 * sampleCountInverse;

    float sum = 0.0;
    float totalSamples = 0.0;
    for (int i = 0; i < sampleCount && i < DISK_SAMPLE_COUNT; ++i)
    {
        // 生成均匀分布的采样点
        float sampleDistNorm;
        float2 offset = ComputeFibonacciSpiralDiskSampleUniform(i, sampleCountInverse, sampleCountBias, sampleDistNorm);

        // 应用 jitter
        offset = float2(offset.x *  sampleJitter.y + offset.y * sampleJitter.x,
                       offset.x * -sampleJitter.x + offset.y * sampleJitter.y);

        // 计算阴影贴图上的偏移
        offset *= samplingFilterSize;
        // 转换到 Cascade 上偏移
        offset *= shadowMapInAtlasScale;

        float2 sampleCoord = shadowCoord.xy + offset;

        // 将着色点的深度进行偏移, 以符合使用直角径来定义方向光的设定 , 这对于消除自遮挡很重要
        float zOffset = filterSize * sampleDistNorm * radial2DepthScale;
        float coordz = shadowCoord.z + Z_OFFSET_DIRECTION * min(zOffset, maxPCSSOffset);

        // 判断是否超过 Cascade 边界
        if (!(any(sampleCoord < minCoord) || any(sampleCoord > maxCoord)))
        {
            const float shadowSample = SAMPLE_TEXTURE2D_SHADOW(shadowMap, sampler_shadowMap, float3(sampleCoord, coordz)).r;
            sum += shadowSample;
            totalSamples += 1.0;
        }
    }

    // totalSamples shall not be zero (at least the center will get sampled)
    return sum / totalSamples;
}

float SampleShadow_PCSS(TEXTURE2D_SHADOW_PARAM(shadowMap, sampler_shadowMap), float4 shadowCoord, float4 shadowMapSize, float2 positionSS)
{
    int cascadeIndex = (int)shadowCoord.w;

    // 当前片元所处 Cascade 在 ShadowMapAtlas 上的起始 uv 坐标
    float2 shadowMapInAtlasOffset = _CascadeOffsetScales[cascadeIndex].xy;
    //当前片元所处 Cascade 在 ShadowMapAtlas 上的宽和高
    float2 shadowMapInAtlasScale = _CascadeOffsetScales[cascadeIndex].zw;

    // 每个 cascade 的 texel 大小 1.0 / cascadeWidth
    float texelSize = shadowMapSize.x / shadowMapInAtlasScale.x;

    float depth2RadialScale = _DirLightPCSSParams0[cascadeIndex].x;            // d_{dirLight} / 2D
    float maxBlockerDistance = _DirLightPCSSParams0[cascadeIndex].z;           // 最大 Blocker 搜索的深度
    float maxSamplingDistance = _DirLightPCSSParams0[cascadeIndex].w;          // 自定义参数 最大搜索深度
    float minFilterRadius = texelSize * _DirLightPCSSParams1[cascadeIndex].x;  // 阴影贴图上最小搜索范围以纹素的大小为单位
    float minFilterRadial2DepthScale = _DirLightPCSSParams1[cascadeIndex].y;   // 2D / d_{Filtering}
    float blockerRadial2DepthScale = _DirLightPCSSParams1[cascadeIndex].z;     // 2D / d_{Blocker}

    // Blocker 搜索的最大深度 D
    float maxSampleZDistance = maxBlockerDistance * abs(_DirLightPCSSProjs[cascadeIndex].z);
    // PCF Filtering 的最大深度 D
    float maxPCSSOffset = maxSamplingDistance * abs(_DirLightPCSSProjs[cascadeIndex].z);

    // 生成采样随机 jitter
    float2 fragCoord = positionSS.xy * _ScreenParams.xy;
    float randomAngle = InterleavedGradientNoise(fragCoord) * (2.0 * PI);
    float2 sampleJitter = float2(cos(randomAngle), sin(randomAngle));

    // 1) Blocker search
    // 根据 Blocker 搜索的最大深度 D 计算出 Blocker 搜索的范围大小
#if UNITY_REVERSED_Z
    float blockSearchFilterSize = max(min(1.0 - shadowCoord.z, maxSampleZDistance) * depth2RadialScale, minFilterRadius);
#else
    float blockSearchFilterSize = max(min(shadowCoord.z, maxSampleZDistance) * depth2RadialScale, minFilterRadius);
#endif

    // 搜索 Blocker
    float blockerDepth = 0.0;
    bool blockerFound = BlockerSearch(blockerDepth, blockSearchFilterSize, shadowCoord, shadowMapInAtlasScale, shadowMapInAtlasOffset,
        sampleJitter, shadowMap, sampler_PointClamp, FIND_BLOCKER_SAMPLE_COUNT, blockerRadial2DepthScale, minFilterRadius, minFilterRadial2DepthScale);

    // 2) 评估半影范围
    float blockerDistance = min(abs(blockerDepth - shadowCoord.z) * 0.9, maxSampleZDistance);
    float filterSize = blockerDistance * depth2RadialScale;
    float samplingFilterSize = max(filterSize, minFilterRadius);
    maxPCSSOffset = min(maxPCSSOffset, blockerDistance * 0.25);

    // 3) PCF 采样
    return blockerFound ? PCF_Filter(TEXTURE2D_SHADOW_ARGS(shadowMap, sampler_shadowMap), shadowCoord, filterSize, shadowMapInAtlasScale, shadowMapInAtlasOffset, sampleJitter, PCSS_SAMPLE_COUNT, minFilterRadial2DepthScale, maxPCSSOffset, samplingFilterSize) : 1.0;
}

#endif
