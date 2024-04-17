using System;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace TinyRenderPipeline.CustomShaderGUI
{
    internal class ParticlesUnlitGUI : ShaderGUI
    {
        #region EnumsAndClasses

        public enum SurfaceType
        {
            Opaque,
            Transparent
        }

        public enum BlendMode
        {
            Alpha,
            Additive
        }

        public enum RenderFace
        {
            Front = 2,
            Back = 1,
            Both = 0
        }

        public enum ZWriteMode
        {
            Off,
            On
        }

        private class Styles
        {
            public static readonly string[] surfaceTypeNames = Enum.GetNames(typeof(SurfaceType));
            public static readonly string[] blendModeNames = Enum.GetNames(typeof(BlendMode));
            public static readonly string[] renderFaceNames = Enum.GetNames(typeof(RenderFace));
            public static readonly string[] zwriteModeNames = Enum.GetNames(typeof(ZWriteMode));

            public static readonly GUIContent surfaceType = EditorGUIUtility.TrTextContent("Surface Type");
            public static readonly GUIContent blendingMode = EditorGUIUtility.TrTextContent("Blending Mode");
            public static readonly GUIContent cullingText = EditorGUIUtility.TrTextContent("Render Face");
            public static readonly GUIContent zwriteText = EditorGUIUtility.TrTextContent("Depth Write");
            public static readonly GUIContent alphaClipText = EditorGUIUtility.TrTextContent("Alpha Clipping");
            public static readonly GUIContent alphaClipThresholdText = new GUIContent("Threshold");

            public static readonly GUIContent baseMap = EditorGUIUtility.TrTextContent("Base Map");

            public static readonly GUIContent flipbookBlending = EditorGUIUtility.TrTextContent("Flip-Book Blending");

            public static readonly GUIContent cameraFadingEnabled = EditorGUIUtility.TrTextContent("Camera Fading");
            public static GUIContent cameraFadingDistanceText = EditorGUIUtility.TrTextContent("Distance");
            public static GUIContent cameraNearFadeDistanceText = EditorGUIUtility.TrTextContent("Near");
            public static GUIContent cameraFarFadeDistanceText = EditorGUIUtility.TrTextContent("Far");


            public static GUIContent softParticlesEnabled = EditorGUIUtility.TrTextContent("Soft Particles");
            public static GUIContent softParticlesFadeText = EditorGUIUtility.TrTextContent("Surface Fade");
            public static GUIContent softParticlesNearFadeDistanceText = EditorGUIUtility.TrTextContent("Near");
            public static GUIContent softParticlesFarFadeDistanceText = EditorGUIUtility.TrTextContent("Far");
        }

        #endregion

        #region Variables

        private MaterialEditor materialEditor;
        private MaterialProperty surfaceTypeProp;
        private MaterialProperty blendModeProp;
        private MaterialProperty cullingProp;
        private MaterialProperty zwriteProp;
        private MaterialProperty baseMapProp;
        private MaterialProperty baseColorProp;
        private MaterialProperty alphaClipProp;
        private MaterialProperty alphaCutoffProp;
        private MaterialProperty flipbookMode;

        private MaterialProperty cameraFading;
        private MaterialProperty cameraNearFadeDistance;
        private MaterialProperty cameraFarFadeDistance;

        private MaterialProperty softParticlesEnabled;
        private MaterialProperty softParticlesNearFadeDistance;
        private MaterialProperty softParticlesFarFadeDistance;

        #endregion

        private void FindProperties(MaterialProperty[] properties)
        {
            var material = materialEditor?.target as Material;
            if (material == null)
                return;

            surfaceTypeProp = FindProperty("_Surface", properties, false);
            blendModeProp = FindProperty("_Blend", properties, false);
            cullingProp = FindProperty("_Cull", properties, false);
            zwriteProp = FindProperty("_ZWrite", properties, false);
            alphaClipProp = FindProperty("__AlphaClip", properties, false);

            alphaCutoffProp = FindProperty("_Cutoff", properties, false);
            baseMapProp = FindProperty("_BaseMap", properties, false);
            baseColorProp = FindProperty("_BaseColor", properties, false);

            flipbookMode = FindProperty("_FlipbookBlending", properties, false);

            cameraFading = FindProperty("_CameraFadingEnabled", properties, false);
            cameraNearFadeDistance = FindProperty("_CameraNearFadeDistance", properties);
            cameraFarFadeDistance = FindProperty("_CameraFarFadeDistance", properties);

            softParticlesEnabled = FindProperty("_SoftParticlesEnabled", properties);
            softParticlesNearFadeDistance = FindProperty("_SoftParticlesNearFadeDistance", properties);
            softParticlesFarFadeDistance = FindProperty("_SoftParticlesFarFadeDistance", properties);
        }

        public override void OnGUI(MaterialEditor materialEditorIn, MaterialProperty[] properties)
        {
            if (materialEditorIn == null)
                throw new ArgumentNullException("materialEditorIn");

            materialEditor = materialEditorIn;
            Material material = materialEditor.target as Material;

            FindProperties(properties);

            ShaderPropertiesGUI(material);

            {
                MaterialProperty[] props = { };
                base.OnGUI(materialEditor, props);
            }
        }

        public override void ValidateMaterial(Material material)
        {
            SetMaterialKeywords(material);
        }

        private void ShaderPropertiesGUI(Material material)
        {
            DoPopup(Styles.surfaceType, surfaceTypeProp, Styles.surfaceTypeNames);
            if ((surfaceTypeProp != null) && ((SurfaceType)surfaceTypeProp.floatValue == SurfaceType.Transparent))
            {
                DoPopup(Styles.blendingMode, blendModeProp, Styles.blendModeNames);
            }

            DoPopup(Styles.cullingText, cullingProp, Styles.renderFaceNames);
            DoPopup(Styles.zwriteText, zwriteProp, Styles.zwriteModeNames);

            DrawFloatToggleProperty(Styles.alphaClipText, alphaClipProp);
            if ((alphaClipProp != null) && (alphaCutoffProp != null) && (alphaClipProp.floatValue == 1))
                materialEditor.ShaderProperty(alphaCutoffProp, Styles.alphaClipThresholdText, 1);

            if (baseMapProp != null && baseColorProp != null)
                materialEditor.TexturePropertySingleLine(Styles.baseMap, baseMapProp, baseColorProp);

            if (baseMapProp != null)
                materialEditor.TextureScaleOffsetProperty(baseMapProp);

            materialEditor.ShaderProperty(flipbookMode, Styles.flipbookBlending);

            // Z write doesn't work with fading
            bool hasZWrite = (material.GetFloat("_ZWrite") > 0.0f);
            if (!hasZWrite)
            {
                // Camera fading
                {
                    materialEditor.ShaderProperty(cameraFading, Styles.cameraFadingEnabled);
                    if (cameraFading.floatValue >= 0.5f)
                    {
                        EditorGUI.indentLevel++;
                        TwoFloatSingleLine(Styles.cameraFadingDistanceText,
                            cameraNearFadeDistance,
                            Styles.cameraNearFadeDistanceText,
                            cameraFarFadeDistance,
                            Styles.cameraFarFadeDistanceText,
                            materialEditor);
                        EditorGUI.indentLevel--;
                    }
                }

                // Soft particles
                {
                    materialEditor.ShaderProperty(softParticlesEnabled, Styles.softParticlesEnabled);
                    if (softParticlesEnabled.floatValue >= 0.5f)
                    {
                        TinyRenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline as TinyRenderPipelineAsset;
                        if (asset != null && !asset.requireDepthTexture)
                        {
                            GUIStyle warnStyle = new GUIStyle(GUI.skin.label);
                            warnStyle.fontStyle = FontStyle.BoldAndItalic;
                            warnStyle.wordWrap = true;
                            EditorGUILayout.HelpBox("Soft Particles require depth texture. Please enable \"Depth Texture\" in the Universal Render Pipeline settings.", MessageType.Warning);
                        }

                        EditorGUI.indentLevel++;
                        TwoFloatSingleLine(Styles.softParticlesFadeText,
                            softParticlesNearFadeDistance,
                            Styles.softParticlesNearFadeDistanceText,
                            softParticlesFarFadeDistance,
                            Styles.softParticlesFarFadeDistanceText,
                            materialEditor);
                        EditorGUI.indentLevel--;
                    }
                }
            }
        }

        private void DoPopup(GUIContent label, MaterialProperty property, string[] options)
        {
            if (property != null)
                materialEditor.PopupShaderProperty(property, label, options);
        }

        private static void DrawFloatToggleProperty(GUIContent styles, MaterialProperty prop, int indentLevel = 0,
            bool isDisabled = false)
        {
            if (prop == null)
                return;

            EditorGUI.BeginDisabledGroup(isDisabled);
            EditorGUI.indentLevel += indentLevel;
            EditorGUI.BeginChangeCheck();
            MaterialEditor.BeginProperty(prop);
            bool newValue = EditorGUILayout.Toggle(styles, prop.floatValue == 1);
            if (EditorGUI.EndChangeCheck())
                prop.floatValue = newValue ? 1.0f : 0.0f;
            MaterialEditor.EndProperty();
            EditorGUI.indentLevel -= indentLevel;
            EditorGUI.EndDisabledGroup();
        }

        private static void SetMaterialKeywords(Material material)
        {
            SetupMaterialBlendModeInternal(material);

            if (material.HasProperty("_Cull"))
                material.doubleSidedGI = (RenderFace)material.GetFloat("_Cull") != RenderFace.Front;

            // Is the material transparent
            bool isTransparent = material.GetTag("RenderType", false) == "Transparent";
            // Z write doesn't work with distortion/fading
            bool hasZWrite = (material.GetFloat("_ZWrite") > 0.0f);

            // Flipbook blending
            if (material.HasProperty("_FlipbookBlending"))
                CoreUtils.SetKeyword(material, "_FLIPBOOKBLENDING_ON", material.GetFloat("_FlipbookBlending") > 0.5f);

            // Camera fading
            var useCameraFading = false;
            if (material.HasProperty("_CameraFadingEnabled") && isTransparent)
            {
                useCameraFading = material.GetFloat("_CameraFadingEnabled") > 0.5f;
                if (useCameraFading)
                {
                    var cameraNearFadeDistance = material.GetFloat("_CameraNearFadeDistance");
                    var cameraFarFadeDistance = material.GetFloat("_CameraFarFadeDistance");
                    // clamp values
                    if (cameraNearFadeDistance < 0.0f)
                    {
                        material.SetFloat("_CameraNearFadeDistance", 0.0f);
                    }

                    if (cameraFarFadeDistance < 0.0f)
                    {
                        material.SetFloat("_CameraFarFadeDistance", 0.0f);
                    }
                }
                var useFading = useCameraFading && !hasZWrite;
                CoreUtils.SetKeyword(material, "_FADING_ON", useFading);
            }

            // Soft particles
            var useSoftParticles = false;
            if (material.HasProperty("_SoftParticlesEnabled") && isTransparent)
            {
                useSoftParticles = (material.GetFloat("_SoftParticlesEnabled") > 0.5f);
                if (useSoftParticles)
                {
                    var softParticlesNearFadeDistance = material.GetFloat("_SoftParticlesNearFadeDistance");
                    var softParticlesFarFadeDistance = material.GetFloat("_SoftParticlesFarFadeDistance");
                    // clamp values
                    if (softParticlesNearFadeDistance < 0.0f)
                    {
                        material.SetFloat("_SoftParticlesNearFadeDistance", 0.0f);
                    }

                    if (softParticlesFarFadeDistance < 0.0f)
                    {
                        material.SetFloat("_SoftParticlesFarFadeDistance", 0.0f);
                    }
                }
                CoreUtils.SetKeyword(material, "_SOFTPARTICLES_ON", useSoftParticles);
            }
        }

        private static void SetupMaterialBlendModeInternal(Material material)
        {
            if (material == null)
                throw new ArgumentNullException("material");

            bool alphaClip = false;
            if (material.HasProperty("__AlphaClip"))
                alphaClip = material.GetFloat("__AlphaClip") >= 0.5f;
            CoreUtils.SetKeyword(material, "_ALPHATEST_ON", alphaClip);

            int renderQueue = material.shader.renderQueue;
            material.SetOverrideTag("RenderType", "");
            if (material.HasProperty("_Surface"))
            {
                SurfaceType surfaceType = (SurfaceType)material.GetFloat("_Surface");
                bool zwrite = false;
                if (surfaceType == SurfaceType.Opaque)
                {
                    if (alphaClip)
                    {
                        renderQueue = (int)RenderQueue.AlphaTest;
                        material.SetOverrideTag("RenderType", "TransparentCutout");
                    }
                    else
                    {
                        renderQueue = (int)RenderQueue.Geometry;
                        material.SetOverrideTag("RenderType", "Opaque");
                    }

                    SetMaterialSrcDstBlendProperties(material, UnityEngine.Rendering.BlendMode.One,
                        UnityEngine.Rendering.BlendMode.Zero);
                    zwrite = true;
                }
                else
                {
                    BlendMode blendMode = (BlendMode)material.GetFloat("_Blend");

                    var srcBlendRGB = UnityEngine.Rendering.BlendMode.One;
                    var dstBlendRGB = UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
                    var srcBlendA = UnityEngine.Rendering.BlendMode.One;
                    var dstBlendA = UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;

                    switch (blendMode)
                    {
                        case BlendMode.Alpha:
                            srcBlendRGB = UnityEngine.Rendering.BlendMode.SrcAlpha;
                            dstBlendRGB = UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
                            srcBlendA = UnityEngine.Rendering.BlendMode.One;
                            dstBlendA = dstBlendRGB;
                            break;

                        case BlendMode.Additive:
                            srcBlendRGB = UnityEngine.Rendering.BlendMode.SrcAlpha;
                            dstBlendRGB = UnityEngine.Rendering.BlendMode.One;
                            srcBlendA = UnityEngine.Rendering.BlendMode.One;
                            dstBlendA = dstBlendRGB;
                            break;
                    }

                    SetMaterialSrcDstBlendProperties(material, srcBlendRGB, dstBlendRGB, srcBlendA, dstBlendA);

                    material.SetOverrideTag("RenderType", "Transparent");
                    zwrite = false;
                    renderQueue = (int)RenderQueue.Transparent;
                }

                if (material.HasProperty("_ZWrite"))
                    material.SetFloat("_ZWrite", zwrite ? 1.0f : 0.0f);
            }

            if (renderQueue != material.renderQueue)
                material.renderQueue = renderQueue;
        }

        private static void SetMaterialSrcDstBlendProperties(Material material,
            UnityEngine.Rendering.BlendMode srcBlend, UnityEngine.Rendering.BlendMode dstBlend)
        {
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)srcBlend);

            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)dstBlend);

            if (material.HasProperty("_SrcBlendAlpha"))
                material.SetFloat("_SrcBlendAlpha", (float)srcBlend);

            if (material.HasProperty("_DstBlendAlpha"))
                material.SetFloat("_DstBlendAlpha", (float)dstBlend);
        }

        private static void SetMaterialSrcDstBlendProperties(Material material,
            UnityEngine.Rendering.BlendMode srcBlendRGB, UnityEngine.Rendering.BlendMode dstBlendRGB,
            UnityEngine.Rendering.BlendMode srcBlendAlpha, UnityEngine.Rendering.BlendMode dstBlendAlpha)
        {
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)srcBlendRGB);

            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)dstBlendRGB);

            if (material.HasProperty("_SrcBlendAlpha"))
                material.SetFloat("_SrcBlendAlpha", (float)srcBlendAlpha);

            if (material.HasProperty("_DstBlendAlpha"))
                material.SetFloat("_DstBlendAlpha", (float)dstBlendAlpha);
        }

        public static void TwoFloatSingleLine(GUIContent title, MaterialProperty prop1, GUIContent prop1Label,
            MaterialProperty prop2, GUIContent prop2Label, MaterialEditor materialEditor, float labelWidth = 30f)
        {
            const int kInterFieldPadding = 2;

            MaterialEditor.BeginProperty(prop1);
            MaterialEditor.BeginProperty(prop2);

            Rect rect = EditorGUILayout.GetControlRect();
            EditorGUI.PrefixLabel(rect, title);

            var indent = EditorGUI.indentLevel;
            var preLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUI.indentLevel = 0;
            EditorGUIUtility.labelWidth = labelWidth;

            Rect propRect1 = new Rect(rect.x + preLabelWidth, rect.y,
                (rect.width - preLabelWidth) * 0.5f - 1, EditorGUIUtility.singleLineHeight);
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = prop1.hasMixedValue;
            var prop1val = EditorGUI.FloatField(propRect1, prop1Label, prop1.floatValue);
            if (EditorGUI.EndChangeCheck())
                prop1.floatValue = prop1val;

            Rect propRect2 = new Rect(propRect1.x + propRect1.width + kInterFieldPadding, rect.y,
                propRect1.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = prop2.hasMixedValue;
            var prop2val = EditorGUI.FloatField(propRect2, prop2Label, prop2.floatValue);
            if (EditorGUI.EndChangeCheck())
                prop2.floatValue = prop2val;

            EditorGUI.indentLevel = indent;
            EditorGUIUtility.labelWidth = preLabelWidth;

            EditorGUI.showMixedValue = false;

            MaterialEditor.EndProperty();
            MaterialEditor.EndProperty();
        }
    }
}
