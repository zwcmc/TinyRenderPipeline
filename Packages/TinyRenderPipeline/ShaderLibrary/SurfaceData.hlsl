#ifndef TINY_RP_SURFACE_DATA_INCLUDED
#define TINY_RP_SURFACE_DATA_INCLUDED

struct SurfaceData
{
    half3 albedo;
    half3 normalTS;
    half3 emission;
    half occlusion;
    half alpha;
    half metallic;
    half smoothness;
};

#endif
