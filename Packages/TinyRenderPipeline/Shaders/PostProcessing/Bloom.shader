Shader "Hidden/Tiny Render Pipeline/Bloom"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZTest Always
        ZWrite Off

        HLSLINCLUDE
        #include "Packages/com.zwcmc.tiny-rp/ShaderLibrary/BlitVertex.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

        TEXTURE2D(_SourceTexLowMip);

        float4 _SourceTexLowMip_TexelSize;
        float4 _BloomParams; // ( x: scatter, y: clamp max, z: threshold, w: threshold knee )

        #define Scatter            _BloomParams.x
        #define ClampMax           _BloomParams.y
        #define Threshold          _BloomParams.z
        #define ThresholdKnee      _BloomParams.w

        half4 FragPrefilter(Varyings input) : SV_TARGET
        {
            float2 uv = input.uv;

        #if defined(_BLOOM_HQ)
            float texelSize = _BlitTexture_TexelSize.x;
            half4 A = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(-1.0, -1.0));
            half4 B = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(0.0, -1.0));
            half4 C = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(1.0, -1.0));
            half4 D = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(-0.5, -0.5));
            half4 E = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(0.5, -0.5));
            half4 F = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(-1.0, 0.0));
            half4 G = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
            half4 H = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(1.0, 0.0));
            half4 I = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(-0.5, 0.5));
            half4 J = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(0.5, 0.5));
            half4 K = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(-1.0, 1.0));
            half4 L = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(0.0, 1.0));
            half4 M = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + texelSize * float2(1.0, 1.0));

            half2 div = (1.0 / 4.0) * half2(0.5, 0.125);

            half4 o = (D + E + I + J) * div.x;
            o += (A + B + G + F) * div.y;
            o += (B + C + H + G) * div.y;
            o += (F + G + L + K) * div.y;
            o += (G + H + M + L) * div.y;

            half3 color = o.xyz;
        #else
            half3 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).xyz;
        #endif

            // User controlled clamp to limit crazy high broken spec
            color = min(ClampMax, color);

            // Thresholding
            half brightness = Max3(color.r, color.g, color.b);
            half softness = clamp(brightness - Threshold + ThresholdKnee, 0.0, 2.0 * ThresholdKnee);
            softness = (softness * softness) / (4.0 * ThresholdKnee + 1e-4);
            half multiplier = max(brightness - Threshold, softness) / max(brightness, 1e-4);
            color *= multiplier;

            // Clamp colors to positive once in prefilter. Encode can have a sqrt, and sqrt(-x) == NaN. Up/Downsample passes would then spread the NaN.
            color = max(color, 0);
            return half4(color, 1.0);
        }

        half4 FragBlurH(Varyings input) : SV_TARGET
        {
            float2 texelSize = _BlitTexture_TexelSize.xy * 2.0;
            float2 uv = input.uv;

            // 9-tap gaussian blur on the downsampled source
            half3 c0 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv - float2(texelSize.x * 4.0, 0.0)).rgb;
            half3 c1 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv - float2(texelSize.x * 3.0, 0.0)).rgb;
            half3 c2 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv - float2(texelSize.x * 2.0, 0.0)).rgb;
            half3 c3 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv - float2(texelSize.x * 1.0, 0.0)).rgb;
            half3 c4 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).rgb;
            half3 c5 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(texelSize.x * 1.0, 0.0)).rgb;
            half3 c6 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(texelSize.x * 2.0, 0.0)).rgb;
            half3 c7 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(texelSize.x * 3.0, 0.0)).rgb;
            half3 c8 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(texelSize.x * 4.0, 0.0)).rgb;

            half3 color = c0 * 0.01621622 + c1 * 0.05405405 + c2 * 0.12162162 + c3 * 0.19459459
                        + c4 * 0.22702703
                        + c5 * 0.19459459 + c6 * 0.12162162 + c7 * 0.05405405 + c8 * 0.01621622;

            return half4(color, 1.0);
        }

        half4 FragBlurV(Varyings input) : SV_TARGET
        {
            float2 texelSize = _BlitTexture_TexelSize.xy;
            float2 uv = input.uv;

            // Optimized bilinear 5-tap gaussian on the same-sized source (9-tap equivalent)
            half3 c0 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv - float2(0.0, texelSize.y * 3.23076923)).rgb;
            half3 c1 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv - float2(0.0, texelSize.y * 1.38461538)).rgb;
            half3 c2 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).rgb;
            half3 c3 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(0.0, texelSize.y * 1.38461538)).rgb;
            half3 c4 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(0.0, texelSize.y * 3.23076923)).rgb;

            half3 color = c0 * 0.07027027 + c1 * 0.31621622
                        + c2.rgb * 0.22702703
                        + c3 * 0.31621622 + c4 * 0.07027027;

            return half4(color, 1.0);
        }

        half4 FragUpsample(Varyings input) : SV_TARGET
        {
            float2 uv = input.uv;

            half3 highMip = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).rgb;

        #if defined(_BLOOM_HQ)
            half3 lowMip = SampleTexture2DBicubic(TEXTURE2D_ARGS(_SourceTexLowMip, sampler_LinearClamp), uv, _SourceTexLowMip_TexelSize.zwxy, (1.0).xx, 0.0).rgb;
        #else
            half3 lowMip = SAMPLE_TEXTURE2D(_SourceTexLowMip, sampler_LinearClamp, uv).rgb;
        #endif

            return half4(lerp(highMip, lowMip, Scatter), 1.0);
        }

        ENDHLSL

        Pass
        {
            Name "Bloom Prefilter"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragPrefilter
            #pragma multi_compile_local _ _BLOOM_HQ
            ENDHLSL
        }

        Pass
        {
            Name "Bloom Blur Horizontal"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlurH
            ENDHLSL
        }

        Pass
        {
            Name "Bloom Blur Vertical"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlurV
            ENDHLSL
        }

        Pass
        {
            Name "Bloom Upsample"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragUpsample
            #pragma multi_compile_local _ _BLOOM_HQ
            ENDHLSL
        }
    }

    FallBack "Hidden/Tiny Render Pipeline/FallbackError"
}
