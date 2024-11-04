Shader "Hidden/Tiny Render Pipeline/ScalableAO"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZTest Always
        ZWrite Off

        HLSLINCLUDE
        #include "Packages/com.zwcmc.tiny-rp/ShaderLibrary/BlitVertex.hlsl"
        #include "Packages/com.zwcmc.tiny-rp/Shaders/ScalableAO/SAO.hlsl"
        ENDHLSL

        Pass
        {
            Name "ScalableAO AO Buffer"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment ScalableAOFragment
            ENDHLSL
        }

        Pass
        {
            Name "ScalableAO Bilateral Blur Horizontal"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment BilateralBlurFragment
            ENDHLSL
        }

        Pass
        {
            Name "ScalableAO Bilateral Blur Vertical"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FinalBilateralBlurFragment
            ENDHLSL
        }
    }

    FallBack "Hidden/Tiny Render Pipeline/FallbackError"
}
