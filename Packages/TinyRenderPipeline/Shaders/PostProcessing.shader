Shader "Hidden/Tiny Render Pipeline/Post Processing"
{
    SubShader
    {
        Cull Off
        ZTest Always
        ZWrite Off

        HLSLINCLUDE
        #pragma target 3.5

        #include "PostProcessingPass.hlsl"
        ENDHLSL

        Pass
        {
            Name "BlitCopy"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
