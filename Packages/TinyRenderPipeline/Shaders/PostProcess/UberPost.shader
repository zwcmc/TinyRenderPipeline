Shader "Hidden/Tiny Render Pipeline/Uber Post"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZTest Always
        ZWrite Off

        Pass
        {
            Name "Uber Post"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragUberPost
            #pragma multi_compile_local_fragment _ _BLOOM
            #pragma multi_compile_local_fragment _ _TONEMAP_ACES _TONEMAP_NEUTRAL

            #include "Packages/com.zwcmc.tiny-rp/ShaderLibrary/BlitVertex.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_Bloom_Texture);
            float _BloomIntensity;

            TEXTURE2D(_InternalLut);
            float4 _Lut_Params;

            half3 ApplyColorGrading(half3 input, TEXTURE2D_PARAM(lutTex, lutSampler), float3 lutParams)
            {
                float3 inputLutSpace = saturate(LinearToLogC(input));  // LUT space is in LogC
                input = ApplyLut2D(TEXTURE2D_ARGS(lutTex, lutSampler), inputLutSpace, lutParams);
                return input;
            }

            half4 FragUberPost(Varyings input) : SV_Target0
            {
                float2 uv = input.uv;

                half3 color = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, uv, 0.0).rgb;

            #if defined(_BLOOM)
                color += SAMPLE_TEXTURE2D_LOD(_Bloom_Texture, sampler_LinearClamp, uv, 0.0).rgb * _BloomIntensity;
            #endif

                // Color grading
                {
                    color = ApplyColorGrading(color, TEXTURE2D_ARGS(_InternalLut, sampler_LinearClamp), _Lut_Params.xyz);
                }

                return half4(color, 1.0);
            }

            ENDHLSL
        }
    }

    FallBack "Hidden/Tiny Render Pipeline/FallbackError"
}
