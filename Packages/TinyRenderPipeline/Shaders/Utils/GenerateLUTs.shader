Shader "Hidden/Tiny Render Pipeline/GenerateLUTs"
{
    SubShader
    {
        HLSLINCLUDE
        #include "../../ShaderLibrary/CustomRenderTexture.hlsl"
        #include "../../ShaderLibrary/MonteCarlo.hlsl"
        ENDHLSL

        Pass
        {
            Name "PBR Specular DFG"

            HLSLPROGRAM
                #pragma vertex CustomRenderTextureVertex
                #pragma fragment IntegrateSpecularDGVFragment

                float2 IntegrateSpecularDGVFragment(Varyings input) : SV_Target0
                {
                    float2 uv = input.localTexcoord.xy;
                    float2 iblDFG = DFV(uv.x, uv.y);
                    return iblDFG;
                }
            ENDHLSL
        }
    }
}
