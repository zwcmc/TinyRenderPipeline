Shader "Hidden/Tiny Render Pipeline/TAA"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        ZTest Always
        ZWrite Off

        HLSLINCLUDE
        #include "Packages/com.zwcmc.tiny-rp/ShaderLibrary/BlitVertex.hlsl"
        #include "TemporalAA.hlsl"
        ENDHLSL

        Pass
        {
            Name "TAA"

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment TemporalAAFragment
            ENDHLSL
        }

        Pass
        {
            Name "TAA Copy History"

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment CopyHistoryFragment
            ENDHLSL
        }
    }

    FallBack "Hidden/Tiny Render Pipeline/FallbackError"
}
