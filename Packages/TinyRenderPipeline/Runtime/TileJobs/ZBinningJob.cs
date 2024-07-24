using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(FloatMode = FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
struct ZBinningJob : IJobFor
{
    // Do not use this for the innerloopBatchCount (use 1 for that). Use for dividing the arrayLength when scheduling.
    public const int batchSize = 128;

    public const int headerLength = 2;

    [NativeDisableParallelForRestriction]
    public NativeArray<uint> bins;

    [ReadOnly]
    public NativeArray<float2> minMaxZs;

    public float zBinScale;

    public float zBinOffset;

    public int binCount;

    public int wordsPerTile;

    public int lightCount;

    public int batchCount;

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
        var batchIndex = jobIndex % batchCount;

        var binStart = batchSize * batchIndex;
        var binEnd = math.min(binStart + batchSize, binCount) - 1;

        var emptyHeader = EncodeHeader(ushort.MaxValue, ushort.MinValue);
        for (var binIndex = binStart; binIndex <= binEnd; binIndex++)
        {
            bins[binIndex * (headerLength + wordsPerTile) + 0] = emptyHeader;
            bins[binIndex * (headerLength + wordsPerTile) + 1] = emptyHeader;
        }

        // Fill ZBins for lights.
        FillZBins(binStart, binEnd, 0, lightCount, 0);
    }

    void FillZBins(int binStart, int binEnd, int itemStart, int itemEnd, int headerIndex)
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
                var (minIndex, maxIndex) = DecodeHeader(bins[baseIndex + headerIndex]);
                minIndex = math.min(minIndex, (uint)index);
                maxIndex = math.max(maxIndex, (uint)index);
                bins[baseIndex + headerIndex] = EncodeHeader(minIndex, maxIndex);
                bins[baseIndex + headerLength + wordIndex] |= bitMask;
            }
        }
    }
}
