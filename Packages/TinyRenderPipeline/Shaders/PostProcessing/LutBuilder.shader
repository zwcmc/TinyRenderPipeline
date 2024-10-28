Shader "Hidden/Tiny Render Pipeline/LutBuilder"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZTest Always
        ZWrite Off

        HLSLINCLUDE

        #pragma multi_compile_local _ _TONEMAP_ACES _TONEMAP_NEUTRAL

        #include "Packages/com.tiny.render-pipeline/ShaderLibrary/BlitVertex.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

        float4 _Lut_Params;        // x: lut_height, y: 0.5 / lut_width, z: 0.5 / lut_height, w: lut_height / lut_height - 1

        float4 _HueSatConPos;      // x: hue shift, y: saturation, z: contrast, w: post exposure
        float4 _ColorFilter;       // xyz: color, w: unused

        float4 _ColorBalance;      // xyz: LMS coeffs, w: unused

        half GetLuminance(half3 colorLinear)
        {
        #if defined(_TONEMAP_ACES)
            return AcesLuminance(colorLinear);
        #else
            return Luminance(colorLinear);
        #endif
        }

        float3 ColorGrade(float3 colorLinear)
        {
            // White balance in LMS space
            float3 colorLMS = LinearToLMS(colorLinear);
            colorLMS *= _ColorBalance.rgb;
            colorLinear = LMSToLinear(colorLMS);

            // Color adjustments: post exposure
            colorLinear *= _HueSatConPos.w;

            // Color adjustments: contrast
            // Do contrast in Log C
        #if defined(_TONEMAP_ACES)
            float3 colorLog = ACES_to_ACEScc(unity_to_ACES(colorLinear));
        #else
            float3 colorLog = LinearToLogC(colorLinear);
        #endif

            colorLog = (colorLog - ACEScc_MIDGRAY) * _HueSatConPos.z + ACEScc_MIDGRAY;

        #if defined(_TONEMAP_ACES)
            colorLinear = ACES_to_ACEScg(ACEScc_to_ACES(colorLog));
        #else
            colorLinear = LogCToLinear(colorLog);
        #endif

            // Color adjustments: color filter
            colorLinear *= _ColorFilter.rgb;

            // Do NOT feed negative values to the following color ops
            colorLinear = max(0.0, colorLinear);

            // Color adjustments: hue shift
            float3 hsv = RgbToHsv(colorLinear);
            float hue = hsv.x + _HueSatConPos.x;
            hsv.x = RotateHue(hue, 0.0, 1.0);
            colorLinear = HsvToRgb(hsv);

            // Color adjustments: saturation
            float luma = GetLuminance(colorLinear);
            colorLinear = luma.xxx + (colorLinear - luma.xxx) * _HueSatConPos.yyy;

            return colorLinear;
        }

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
            // Lut input color in Log C space
            // We use Alexa Log C (El 1000) to store the LUT as it provides a good enough range
            // (~58.85666) and is good enough to be stored in fp16 without losing precision in the darks
            float3 colorLutSpace = GetLutStripValue(input.texcoord, _Lut_Params);

            // Switch from Log C space to linear space
            float3 colorLinear = LogCToLinear(colorLutSpace);

            // Color grading
            float3 gradedColor = ColorGrade(colorLinear);

            // Tonemapping
            gradedColor = Tonemap(gradedColor);

            return float4(gradedColor, 1.0);
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

    FallBack "Hidden/Tiny Render Pipeline/FallbackError"
}
