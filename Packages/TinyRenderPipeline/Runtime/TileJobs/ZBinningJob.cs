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

    // 每个 ZBin 起始 1 个 uint 的 header0 和 header1
    public const int headerLength = 1;

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

        // 将2个 16 位的无符号整数编码成一个 32 位的无符号整数，其中 ushort.MaxValue = 65535 存储在低 16 位，ushort.MinValue = 0 存储在高 16 位
        var emptyHeader = EncodeHeader(ushort.MaxValue, ushort.MinValue);
        for (var binIndex = binStart; binIndex <= binEnd; binIndex++)
        {
            // 每个 ZBin 起始的 header 的 index
            var zBinHeaderIndex = binIndex * (headerLength + wordsPerTile);
            bins[zBinHeaderIndex] = emptyHeader;
        }

        // 向每个 ZBin 中填入额外光源的信息
        FillZBins(binStart, binEnd, 0, lightCount);
    }

    void FillZBins(int binStart, int binEnd, int itemStart, int itemEnd)
    {
        // 遍历所有额外光源
        for (var index = itemStart; index < itemEnd; index++)
        {
            // 此光源在观察空间的最小深度 z 和 最大深度 z
            var minMax = minMaxZs[index];

            // 根据此光源的最小深度 z 计算出它处于那个 zBinIndex，与当前批处理的起始 zBinIndexStart 对比取最大值为光源包围盒内所有 zBin 中最小 zBinIndex
            var minBin = math.max((int)((isOrthographic ? minMax.x : math.log2(minMax.x)) * zBinScale + zBinOffset), binStart);
            // 根据此光源的最大深度 z 计算出它处于那个 zBinIndex，与当前批处理的结束 zBinIndexEnd 对比取最小值为光源包围盒内所有 zBin 中最大的 zBinIndex
            var maxBin = math.min((int)((isOrthographic ? minMax.y : math.log2(minMax.y)) * zBinScale + zBinOffset), binEnd);

            // 已经知道一个 zBin 的结构是由一个 32 位无符号整数（uint）的 header 和 N 个 32 位无符号整数（uint）的 “Word” 组成
            // 其中 header 的低 16 位 存储的是此 zBin 所处的所有光源内的光源最小索引，高 16 位存储的是此 zBin 所处的所有光源内的光源最大索引
            // 而每个 “Word” 的 32 位用来存储每个额外光源的位掩码，一个 “Word” 的 32 位可以用来生成 32 个额外光源的索引的位掩码

            // 使用整数除法（即只取商，不取余数），根据当前光源的索引来确定此光源属于第几个 “Word”
            var wordIndex = index / 32;
            // index % 32: 这是取 index 除以32的余数，结果范围是 0 到 31。这个操作是为了确定 index 位在其所属32位字中的具体位置
            // 1u << (index % 32): 这部分是将无符号整数 1 左移 (index % 32) 位。左移操作会将 1 移动到指定的位置，生成一个对应于 index 位的位掩码
            // 例如：
            //      当 index 为 0 时，index % 32 为 0，bitMask 为 00000000 00000000 00000000 00000001
            //      当 index 为 1 时，index % 32 为 1，bitMask 为 00000000 00000000 00000000 00000010
            //      当 index 为 31 时，index % 32 为 31，bitMask 为 10000000 00000000 00000000 00000000
            // 也就是生成当前光源索引的位掩码
            var bitMask = 1u << (index % 32);

            // 遍历此光源包围盒内的所有 zBin
            for (int binIndex = minBin; binIndex <= maxBin; binIndex++)
            {
                // 确定此 zBin 的 header 索引
                var binHeaderIndex = binIndex * (headerLength + wordsPerTile);
                // 解码存储的最小光源索引和最大光源索引
                var (minIndex, maxIndex) = DecodeHeader(bins[binHeaderIndex]);

                // 最小光源索引存储的永远是最小的索引
                minIndex = math.min(minIndex, (uint)index);
                // 最大光源索引存储的永远是最大的索引
                maxIndex = math.max(maxIndex, (uint)index);

                // 把最小和最大光源索引继续编码成一个 32 位无符号整数（uint），其中最小光源索引存储在低 16 位，最大光源索引存储在高 16 位
                bins[binHeaderIndex] = EncodeHeader(minIndex, maxIndex);
                // 位或操作，将此光源所属的 “Word” 中对应此光源索引的位设置为 1
                bins[binHeaderIndex + headerLength + wordIndex] |= bitMask;
            }
        }
    }
}
