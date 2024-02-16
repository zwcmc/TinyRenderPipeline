Shader "Tiny Render Pipeline/Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (0.5, 0.5, 0.5, 1.0)
        _Cutoff("AlphaCutout", Range(0.0, 1.0)) = 0.5

        _Metallic("Metallic", Range(0, 1)) = 0
        _Smoothness("Smoothness", Range(0, 1)) = 0.5

        _Surface("__surface", Float) = 0.0
        _Blend("__mode", Float) = 0.0
        _Cull("__cull", Float) = 2.0
        [ToggleUI] _AlphaClip("__clip", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "IgnoreProjector" = "True"
        }

        Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
        ZWrite [_ZWrite]
        Cull [_Cull]

        Pass
        {
            Name "TinyRP Lit"

            Tags { "LightMode" = "TinyRPLit" }

            HLSLPROGRAM
            #pragma target 3.5

            #pragma vertex LitVertex
            #pragma fragment LitFragment

            #pragma shader_feature_local_fragment _ALPHATEST_ON

            #include "LitInput.hlsl"
            #include "LitForwardPass.hlsl"

            ENDHLSL
        }
    }

    CustomEditor "TinyRenderPipeline.CustomShaderGUI.LitGUI"
}
