Shader "Tiny Render Pipeline/Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (0.5, 0.5, 0.5, 1.0)

        _Smoothness("Smoothness", Range(0, 1)) = 0.5
        _Metallic("Metallic", Range(0, 1)) = 0.0
        _MetallicGlossMap("Metallic", 2D) = "white" {}

        _BumpScale("Scale", Float) = 1.0
        _BumpMap("Normal Map", 2D) = "bump" {}

        [Toggle] _EmissionEnabled("Emission Enabled", Float) = 0.0
        [HDR] _EmissionColor("Color", Color) = (0,0,0)
        _EmissionMap("Emission", 2D) = "white" {}

        _IBL_DFG("DFG LUT", 2D) = "black" {}

        _Surface("__surface", Float) = 0.0
        _Blend("__mode", Float) = 0.0
        _Cull("__cull", Float) = 2.0
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
            Name "TinyRP Forward Lit"
            Tags { "LightMode" = "TinyRPLit" }

            HLSLPROGRAM
            #pragma vertex LitVertex
            #pragma fragment LitFragment

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _METALLICGLOSSMAP

            #pragma multi_compile_fragment _ _SHADOWS_PCF _SHADOWS_PCSS

            #include "Packages/com.tiny.render-pipeline/Shaders/LitInput.hlsl"
            #include "Packages/com.tiny.render-pipeline/Shaders/LitForwardPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Shadow"
            Tags { "LightMode" = "ShadowCaster" }
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment

            #include "Packages/com.tiny.render-pipeline/Shaders/ShadowPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "TinyRP Depth"
            Tags { "LightMode" = "TinyRPDepth" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM

            #pragma vertex DepthVertex
            #pragma fragment DepthFragment

            #include "Packages/com.tiny.render-pipeline/Shaders/DepthOnlyPass.hlsl"

            ENDHLSL
        }
    }

    FallBack "Hidden/Tiny Render Pipeline/FallbackError"
    CustomEditor "TinyRenderPipeline.CustomShaderGUI.LitGUI"
}
