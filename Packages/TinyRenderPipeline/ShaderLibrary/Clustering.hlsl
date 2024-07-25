#ifndef TINY_RP_CLUSTERING_INCLUDED
#define TINY_RP_CLUSTERING_INCLUDED

#include "Packages/com.tiny.render-pipeline/ShaderLibrary/Input.hlsl"

#ifdef _FORWARD_PLUS

// internal
struct ClusterIterator
{
    uint tileOffset;
    uint zBinOffset;
    uint tileMask;
    // Stores the next light index in first 16 bits, and the max light index in the last 16 bits.
    uint entityIndexNextMax;
};

ClusterIterator ClusterInit(float2 normalizedScreenSpaceUV, float3 positionWS, int headerIndex)
{
    ClusterIterator state = (ClusterIterator)0;

    uint2 tileId = uint2(normalizedScreenSpaceUV * URP_FP_TILE_SCALE);
    state.tileOffset = tileId.y * URP_FP_TILE_COUNT_X + tileId.x;
    state.tileOffset *= URP_FP_WORDS_PER_TILE;

    float viewZ = dot(GetViewForwardDir(), positionWS - GetCameraPositionWS());
    uint zBinBaseIndex = (uint)((IsPerspectiveProjection() ? log2(viewZ) : viewZ) * URP_FP_ZBIN_SCALE + URP_FP_ZBIN_OFFSET);

    // The Zbin buffer is laid out in the following manner:
    //                          ZBin 0                                      ZBin 1
    //  .-------------------------^------------------------. .----------------^-------
    // | header0 | header1 | word 1 | word 2 | ... | word N | header0 | header 1 | ...
    //                     `----------------v--------------'
    //                            URP_FP_WORDS_PER_TILE
    //
    // The total length of this buffer is `4*MAX_ZBIN_VEC4S`. `zBinBaseIndex` should
    // always point to the `header 0` of a ZBin, so we clamp it accordingly, to
    // avoid out-of-bounds indexing of the ZBin buffer.
    zBinBaseIndex = zBinBaseIndex * (1 + URP_FP_WORDS_PER_TILE);
    zBinBaseIndex = min(zBinBaseIndex, 4 * MAX_ZBIN_VEC4S - (1 + URP_FP_WORDS_PER_TILE));

    uint zBinHeaderIndex = zBinBaseIndex + headerIndex;
    state.zBinOffset = zBinBaseIndex + 1;

    uint header = Select4(asuint(urp_ZBins[zBinHeaderIndex / 4]), zBinHeaderIndex % 4);
    uint tileIndex = state.tileOffset;
    uint zBinIndex = state.zBinOffset;
    if (URP_FP_WORDS_PER_TILE > 0)
    {
        state.tileMask =
            Select4(asuint(urp_Tiles[tileIndex / 4]), tileIndex % 4) &
            Select4(asuint(urp_ZBins[zBinIndex / 4]), zBinIndex % 4) &
            (0xFFFFFFFFu << (header & 0x1F)) & (0xFFFFFFFFu >> (31 - (header >> 16)));
    }

    return state;
}

// internal
bool ClusterNext(inout ClusterIterator it, out uint entityIndex)
{
    bool hasNext = it.tileMask != 0;
    uint bitIndex = FIRST_BIT_LOW(it.tileMask);
    it.tileMask ^= (1 << bitIndex);
    entityIndex = bitIndex;
    return hasNext;
}

#endif

#endif
