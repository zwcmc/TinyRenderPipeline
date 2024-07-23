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
            #pragma target 3.5

            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _OUTPUT_DEPTH

            #include "Packages/com.tiny.render-pipeline/ShaderLibrary/BlitVertex.hlsl"

            TEXTURE2D_FLOAT(_CameraDepthAttachment);
            SAMPLER(sampler_CameraDepthAttachment);

        #if defined(_OUTPUT_DEPTH)
            float Frag(Varyings input) : SV_Depth
        #else
            float Frag(Varyings input) : SV_Target
        #endif
            {
                return SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthAttachment, sampler_CameraDepthAttachment, input.texcoord, 0.0);
            }

            ENDHLSL
        }
    }

    FallBack "Hidden/Tiny Render Pipeline/FallbackError"
}
