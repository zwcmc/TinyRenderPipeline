Shader "Tiny Render Pipeline/Particles/Unlit"
{
    Properties
    {
        [MainTexture] _BaseMap("Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (0.75, 0.75, 0.75, 1.0)
        _Cutoff("AlphaCutout", Range(0.0, 1.0)) = 0.5

        _CameraNearFadeDistance("Camera Near Fade", Float) = 1.0
        _CameraFarFadeDistance("Camera Far Fade", Float) = 2.0

        _Surface("__surface", Float) = 0.0
        _Blend("__mode", Float) = 0.0
        _Cull("__cull", Float) = 2.0
        [ToggleUI] _AlphaClip("__clip", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [Toggle] _FlipbookBlending("__flipbookblending", Float) = 0.0
        [Toggle] _CameraFadingEnabled("__camerafadingenabled", Float) = 0.0
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
            Name "TinyRP Particle Unlit"

            Tags { "LightMode" = "TinyRPUnlit" }

            HLSLPROGRAM
            #pragma target 3.5

            #pragma vertex ParticleUnlitVertex
            #pragma fragment ParticleUnlitFragment

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local _FLIPBOOKBLENDING_ON
            #pragma shader_feature_local _FADING_ON

            #include "Packages/com.tiny.render-pipeline/Shaders/Particles/ParticlesUnlitInput.hlsl"
            #include "Packages/com.tiny.render-pipeline/Shaders/Particles/ParticlesUnlitForwardPass.hlsl"

            ENDHLSL
        }
    }

    CustomEditor "TinyRenderPipeline.CustomShaderGUI.ParticlesUnlitGUI"
}
