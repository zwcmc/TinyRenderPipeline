#ifndef TINY_RP_LIT_FORWARD_PASS_INCLUDED
#define TINY_RP_LIT_FORWARD_PASS_INCLUDED

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 texcoord   : TEXCOORD0;
};

struct Varyings
{
    float2 uv         : TEXCOORD0;
    float3 normalWS   : TEXCOORD1;
    float3 positionWS : TEXCOORD2;
    half3 vertexSH    : TEXCOORD3;
    float4 positionCS : SV_POSITION;
};

void InitializeInputData(Varyings input, half3 normalTS, out InputData inputData)
{
    inputData = (InputData)0;

    inputData.positionWS = input.positionWS;
    inputData.normalWS = NormalizeNormalPerPixel(input.normalWS); // or normalTS
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);

    inputData.bakedGI = SampleSH(inputData.normalWS);
}

Varyings LitVertex(Attributes input)
{
    Varyings output = (Varyings)0;

    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);

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
