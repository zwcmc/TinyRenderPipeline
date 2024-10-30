#ifndef TINY_RP_LIT_FORWARD_PASS_INCLUDED
#define TINY_RP_LIT_FORWARD_PASS_INCLUDED

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float2 texcoord   : TEXCOORD0;
};

struct Varyings
{
    float2 uv         : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 normalWS   : TEXCOORD2;
#ifdef _NORMALMAP
    half4 tangentWS   : TEXCOORD3;
#endif
    float4 positionCS : SV_POSITION;
};

void InitializeInputData(Varyings input, half3 normalTS, out InputData inputData)
{
    inputData = (InputData)0;

    inputData.positionWS = input.positionWS;

#ifdef _NORMALMAP
    float sign = input.tangentWS.w;
    float3 bitangent = sign * cross(input.normalWS.xyz, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz);
    inputData.normalWS = TransformTangentToWorld(normalTS, tangentToWorld);
#else
    inputData.normalWS = input.normalWS;
#endif
    inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);

    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);

    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
}

Varyings LitVertex(Attributes input)
{
    Varyings output = (Varyings)0;

    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);

    output.positionWS = TransformObjectToWorld(input.positionOS.xyz);

    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
#ifdef _NORMALMAP
    real sign = input.tangentOS.w * GetOddNegativeScale();
    float3 tangentWS = TransformObjectToWorldDir(tangentOS.xyz);
    output.tangentWS = half4(tangentWS, sign);
#endif

    output.positionCS = TransformWorldToHClip(output.positionWS);
    return output;
}

half4 LitFragment(Varyings input) : SV_Target
{
    SurfaceData surfaceData;
    InitializeSurfaceData(input.uv, surfaceData);

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);

    half4 color = SurfaceShading(inputData, surfaceData);

    color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent(_Surface));

    return color;
}

#endif
