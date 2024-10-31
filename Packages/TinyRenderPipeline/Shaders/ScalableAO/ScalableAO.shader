Shader "Hidden/Tiny Render Pipeline/ScalableAO"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZTest Always
        ZWrite Off

        HLSLINCLUDE
        #include "Packages/com.tiny.render-pipeline/ShaderLibrary/BlitVertex.hlsl"
        #include "Packages/com.tiny.render-pipeline/Shaders/ScalableAO/SAO.hlsl"
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
            Name "ScalableAO Bilateral Blur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment BilateralBlurFragment
            ENDHLSL
        }

        Pass
        {
            Name "ScalableAO Final Bilateral Blur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FinalBilateralBlurFragment
            ENDHLSL
        }
    }

    FallBack "Hidden/Tiny Render Pipeline/FallbackError"
}
