Shader "Hidden/Tiny Render Pipeline/FXAA"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZTest Always
        ZWrite Off

        Pass
        {
            Name "FXAA"

            HLSLPROGRAM
            #include "Packages/com.zwcmc.tiny-rp/ShaderLibrary/BlitVertex.hlsl"
            #include "FXAA.hlsl"

            #pragma vertex Vert
            #pragma fragment FragFXAA

            ENDHLSL
        }
    }

    FallBack "Hidden/Tiny Render Pipeline/FallbackError"
}
