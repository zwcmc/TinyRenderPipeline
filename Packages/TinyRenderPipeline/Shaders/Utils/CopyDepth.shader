Shader "Hidden/Tiny Render Pipeline/CopyDepth"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        ZTest Always
        ZWrite On
        ColorMask R
        Cull Off

        Pass
        {
            Name "Copy Depth"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _OUTPUT_DEPTH

            #include "Packages/com.zwcmc.tiny-rp/ShaderLibrary/BlitVertex.hlsl"

            TEXTURE2D_FLOAT(_CameraDepthAttachment);
            SAMPLER(sampler_CameraDepthAttachment);

        #if defined(_OUTPUT_DEPTH)
            float Frag(Varyings input) : SV_Depth
        #else
            float Frag(Varyings input) : SV_TARGET
        #endif
            {
                return SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthAttachment, sampler_CameraDepthAttachment, input.uv, 0.0);
            }

            ENDHLSL
        }
    }

    FallBack "Hidden/Tiny Render Pipeline/FallbackError"
}
