Shader "Hidden/Tiny Render Pipeline/Blit"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZTest Always
        ZWrite Off

        Pass
        {
            Name "Blit Copy"

            HLSLPROGRAM
            #pragma target 3.5

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.tiny.render-pipeline/ShaderLibrary/BlitVertex.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                return SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, uv, 0.0);
            }

            ENDHLSL
        }
    }
}
