using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile(FloatMode = FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
struct ZBinningJob : IJobFor
{
    // 每个处理批次的大小
    public const int batchSize = 128;

    // 每个 ZBin 起始 2 个 uint 的 header0 和 header1
    public const int headerLength = 2;

    // 长度为 TinyRenderPipeline.maxZBinWords 的输出 bins
    [NativeDisableParallelForRestriction]
    public NativeArray<uint> bins;

    // 每个额外光源范围在观察空间的最小 z 值和最大 z 值
    [ReadOnly]
    public NativeArray<float2> minMaxZs;

    // zBinScale 和 zBinOffset 用来计算，观察空间下深度 z 的某个坐标处于哪个 ZBin
    // 正交相机时，zBinIndex = z * zBinScale + zBinOffset; 透视相机时，zBinIndex = log2(z) * zBinScale + zBinOffset;
    public float zBinScale;
    public float zBinOffset;

    // 总的 ZBin 数量
    public int binCount;

    // 每个 ZBin 有多少个 "Word"
    public int wordsPerTile;

    // 额外光源的数量
    public int lightCount;

    // 批处理的次数
    public int batchCount;

    // 相机是正交相机还是透视相机
    public bool isOrthographic;

    static uint EncodeHeader(uint min, uint max)
    {
        return (min & 0xFFFF) | ((max & 0xFFFF) << 16);
    }

    static (uint, uint) DecodeHeader(uint zBin)
    {
        return (zBin & 0xFFFF, (zBin >> 16) & 0xFFFF);
    }

    public void Execute(int jobIndex)
    {
        // 第几次批处理, batchIndex = 0, 1, 2, 3, ...
        var batchIndex = jobIndex % batchCount;

        // 每次批处理一共处理 batchSize 个 ZBin，所以根据 batchIndex 计算出 ZBin 的起始和结束 index，
        // 例如 batchIndex = 0，binStart = 0，binEnd = 127
        var binStart = batchSize * batchIndex;
        var binEnd = math.min(binStart + batchSize, binCount) - 1;

        var emptyHeader = EncodeHeader(ushort.MaxValue, ushort.MinValue);
        for (var binIndex = binStart; binIndex <= binEnd; binIndex++)
        {
            bins[binIndex * (headerLength + wordsPerTile)] = emptyHeader;
        }

        // Fill ZBins for lights.
        FillZBins(binStart, binEnd, 0, lightCount);
    }

    void FillZBins(int binStart, int binEnd, int itemStart, int itemEnd)
    {
        for (var index = itemStart; index < itemEnd; index++)
        {
            var minMax = minMaxZs[index];
            var minBin = math.max((int)((isOrthographic ? minMax.x : math.log2(minMax.x)) * zBinScale + zBinOffset), binStart);
            var maxBin = math.min((int)((isOrthographic ? minMax.y : math.log2(minMax.y)) * zBinScale + zBinOffset), binEnd);

            var wordIndex = index / 32;
            var bitMask = 1u << (index % 32);

            for (int binIndex = minBin; binIndex <= maxBin; binIndex++)
            {
                var baseIndex = binIndex * (headerLength + wordsPerTile);
                var (minIndex, maxIndex) = DecodeHeader(bins[baseIndex]);
                minIndex = math.min(minIndex, (uint)index);
                maxIndex = math.max(maxIndex, (uint)index);
                bins[baseIndex] = EncodeHeader(minIndex, maxIndex);
                bins[baseIndex + headerLength + wordIndex] |= bitMask;
            }
        }
    }
}
