#ifndef TINY_RP_LIT_FORWARD_PASS_INCLUDED
#define TINY_RP_LIT_FORWARD_PASS_INCLUDED

struct Attributes
{
    float3 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 uv         : TEXCOORD0;
};

struct Varyings
{
    float2 uv         : TEXCOORD0;
    float3 normalWS   : TEXCOORD1;
    float3 positionWS : TEXCOORD2;
    float4 positionCS : SV_POSITION;
};

void InitializeInputData(Varyings input, half3 normalTS, out InputData inputData)
{
    inputData = (InputData)0;

    inputData.positionWS = input.positionWS;
    inputData.normalWS = NormalizeNormalPerPixel(input.normalWS); // or normalTS
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
}

Varyings LitVertex(Attributes input)
{
    Varyings output = (Varyings)0;

    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

    output.normalWS = TransformObjectToWorldNormal(input.normalOS.xyz);
    output.positionWS = TransformObjectToWorld(input.positionOS.xyz);

    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    return output;
}

half4 LitFragment(Varyings input) : SV_TARGET
{
    SurfaceData surfaceData;
    InitializeSurfaceData(input.uv, surfaceData);

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);

    half4 color = FragmentPBR(inputData, surfaceData);

    color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent(_Surface));

    return color;
}

#endif
