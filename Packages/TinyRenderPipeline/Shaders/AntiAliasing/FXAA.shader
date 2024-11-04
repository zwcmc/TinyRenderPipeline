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
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"
            #include "Packages/com.zwcmc.tiny-rp/Shaders/AntiAliasing/FXAA.hlsl"

            #pragma vertex Vert
            #pragma fragment FragFXAA

            ENDHLSL
        }
    }

    FallBack "Hidden/Tiny Render Pipeline/FallbackError"
}
