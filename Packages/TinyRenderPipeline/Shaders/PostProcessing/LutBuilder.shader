Shader "Hidden/Tiny Render Pipeline/LutBuilder"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZTest Always
        ZWrite Off

        HLSLINCLUDE
        #pragma target 3.5

        #pragma multi_compile_local _ _TONEMAP_ACES _TONEMAP_NEUTRAL

        #include "Packages/com.tiny.render-pipeline/ShaderLibrary/BlitVertex.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

        float4 _Lut_Params;  // x: lut_height, y: 0.5 / lut_width, z: 0.5 / lut_height, w: lut_height / lut_height - 1

        float3 Tonemap(float3 colorLinear)
        {

        #if defined(_TONEMAP_NEUTRAL)
            colorLinear = NeutralTonemap(colorLinear);
        #elif defined(_TONEMAP_ACES)
            float3 aces = ACEScg_to_ACES(colorLinear);
            colorLinear = AcesTonemap(aces);
        #endif

            return colorLinear;
        }

        float4 FragLutBuilder(Varyings input) : SV_Target
        {
            float3 colorLutSpace = GetLutStripValue(input.texcoord, _Lut_Params);

            // Switch to linear space
            float3 colorLinear = LogCToLinear(colorLutSpace);

            // Tonemapping
            colorLinear = Tonemap(colorLinear);

            return float4(colorLinear, 1.0);
        }

        ENDHLSL

        Pass
        {
            Name "LutBuilder"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragLutBuilder
            ENDHLSL
        }
    }
}
