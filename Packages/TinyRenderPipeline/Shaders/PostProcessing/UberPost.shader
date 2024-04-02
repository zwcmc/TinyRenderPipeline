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
            #pragma multi_compile_local _ _BLOOM_ACTIVED

            #include "Packages/com.tiny.render-pipeline/ShaderLibrary/BlitVertex.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

            TEXTURE2D(_Bloom_Texture);
            float _BloomIntensity;

            half4 FragUberPost(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                half3 color = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;

            #if defined(_BLOOM_ACTIVED)
                color += SAMPLE_TEXTURE2D_LOD(_Bloom_Texture, sampler_LinearClamp, uv, 0).rgb * _BloomIntensity;
            #endif

                return half4(color, 1.0);
            }

            ENDHLSL
        }
    }
}
