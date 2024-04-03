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
            #pragma target 3.5

            #pragma vertex Vert
            #pragma fragment FragUberPost
            #pragma multi_compile_local_fragment _ _BLOOM_ACTIVED
            #pragma multi_compile_local_fragment _ _TONEMAP_ACES _TONEMAP_NEUTRAL

            #include "Packages/com.tiny.render-pipeline/ShaderLibrary/BlitVertex.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_Bloom_Texture);
            float _BloomIntensity;

            half3 ApplyTonemap(half3 input)
            {
            #if defined(_TONEMAP_ACES)
                float3 aces = unity_to_ACES(input);
                input = AcesTonemap(aces);
            #elif defined(_TONEMAP_NEUTRAL)
                input = NeutralTonemap(input);
            #endif

                return saturate(input);
            }

            half4 FragUberPost(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                half3 color = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;

            #if defined(_BLOOM_ACTIVED)
                color += SAMPLE_TEXTURE2D_LOD(_Bloom_Texture, sampler_LinearClamp, uv, 0).rgb * _BloomIntensity;
            #endif

                // Apply tonemapping
                {
                    color = ApplyTonemap(color);
                }

                return half4(color, 1.0);
            }

            ENDHLSL
        }
    }
}
