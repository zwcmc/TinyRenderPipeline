using System;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace TinyRenderPipeline.CustomShaderGUI
{
    internal class LitGUI : ShaderGUI
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

            public static readonly GUIContent surfaceType = EditorGUIUtility.TrTextContent("Surface Type", "");
            public static readonly GUIContent blendingMode = EditorGUIUtility.TrTextContent("Blending Mode", "");
            public static readonly GUIContent cullingText = EditorGUIUtility.TrTextContent("Render Face", "");
            public static readonly GUIContent zwriteText = EditorGUIUtility.TrTextContent("Depth Write", "");

            public static readonly GUIContent baseMap = EditorGUIUtility.TrTextContent("Base Map", "");

            public static GUIContent smoothnessText = EditorGUIUtility.TrTextContent("Smoothness", "");
            public static GUIContent metallicMapText = EditorGUIUtility.TrTextContent("Metallic Map", "");

            public static GUIContent normalMapText = EditorGUIUtility.TrTextContent("Normal Map", "");

            public static GUIContent occlusionText = EditorGUIUtility.TrTextContent("Occlusion Map", "");

            public static readonly GUIContent emissionEnabled = EditorGUIUtility.TrTextContent("Emission", "");
            public static readonly GUIContent emissionMap = EditorGUIUtility.TrTextContent("Emission Map", "");

            public static readonly GUIContent iblDFG = EditorGUIUtility.TrTextContent("IBL DFG Term", "");
        }

        private static class Property
        {
            public static readonly string SurfaceType = "_Surface";
            public static readonly string BlendMode = "_Blend";
            public static readonly string SrcBlend = "_SrcBlend";
            public static readonly string DstBlend = "_DstBlend";
            public static readonly string SrcBlendAlpha = "_SrcBlendAlpha";
            public static readonly string DstBlendAlpha = "_DstBlendAlpha";
            public static readonly string ZWrite = "_ZWrite";
            public static readonly string CullMode = "_Cull";
            public static readonly string BaseMap = "_BaseMap";
            public static readonly string BaseColor = "_BaseColor";

            public static readonly string Smoothness = "_Smoothness";
            public static readonly string Metallic = "_Metallic";
            public static readonly string MetallicGlossMap = "_MetallicGlossMap";

            public static readonly string BumpScale = "_BumpScale";
            public static readonly string BumpMap = "_BumpMap";

            public static readonly string OcclusionStrength = "_OcclusionStrength";
            public static readonly string OcclusionMap = "_OcclusionMap";

            public static readonly string EmissionEnabled = "_EmissionEnabled";
            public static readonly string EmissionColor = "_EmissionColor";
            public static readonly string EmissionMap = "_EmissionMap";

            public static readonly string _IBL_DFG = "_IBL_DFG";
        }

        private static class ShaderKeywordStrings
        {
            public const string _METALLICGLOSSMAP = "_METALLICGLOSSMAP";
            public const string _NORMALMAP = "_NORMALMAP";
            public const string _OCCLUSIONMAP = "_OCCLUSIONMAP";
            public const string _EMISSION = "_EMISSION";
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

        private MaterialProperty smoothnessProp;
        private MaterialProperty metallicProp;
        private MaterialProperty metallicGlossMap;

        private MaterialProperty bumpScaleProp;
        private MaterialProperty bumpMapProp;

        private MaterialProperty occlusionStrength;
        private MaterialProperty occlusionMap;

        private MaterialProperty emissionEnabledProp;
        private MaterialProperty emissionColorProp;
        private MaterialProperty emissionMapProp;

        private MaterialProperty _IBL_DFG;

        #endregion

        private void FindProperties(MaterialProperty[] properties)
        {
            var material = materialEditor?.target as Material;
            if (material == null)
                return;

            surfaceTypeProp = FindProperty(Property.SurfaceType, properties, false);
            blendModeProp = FindProperty(Property.BlendMode, properties, false);
            cullingProp = FindProperty(Property.CullMode, properties, false);
            zwriteProp = FindProperty(Property.ZWrite, properties, false);

            baseMapProp = FindProperty(Property.BaseMap, properties, false);
            baseColorProp = FindProperty(Property.BaseColor, properties, false);

            smoothnessProp = FindProperty(Property.Smoothness, properties, false);
            metallicProp = FindProperty(Property.Metallic, properties, false);
            metallicGlossMap = FindProperty(Property.MetallicGlossMap, properties, false);

            bumpScaleProp = FindProperty(Property.BumpScale, properties, false);
            bumpMapProp = FindProperty(Property.BumpMap, properties, false);

            occlusionStrength = FindProperty(Property.OcclusionStrength, properties, false);
            occlusionMap = FindProperty(Property.OcclusionMap, properties, false);

            emissionEnabledProp = FindProperty(Property.EmissionEnabled, properties, false);
            emissionColorProp = FindProperty(Property.EmissionColor, properties, false);
            emissionMapProp = FindProperty(Property.EmissionMap, properties, false);

            _IBL_DFG = FindProperty(Property._IBL_DFG, properties, false);
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
                MaterialProperty[] props = {};
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

            if (baseMapProp != null && baseColorProp != null)
                materialEditor.TexturePropertySingleLine(Styles.baseMap, baseMapProp, baseColorProp);

            if (metallicGlossMap != null)
                materialEditor.TexturePropertySingleLine(Styles.metallicMapText, metallicGlossMap, (metallicGlossMap.textureValue != null) ? null : metallicProp);

            EditorGUI.indentLevel += 2;
            materialEditor.ShaderProperty(smoothnessProp, Styles.smoothnessText);
            EditorGUI.indentLevel -= 2;

            if (bumpMapProp != null)
                materialEditor.TexturePropertySingleLine(Styles.normalMapText, bumpMapProp, (bumpMapProp.textureValue != null) ? bumpScaleProp : null);

            if (occlusionMap != null)
                materialEditor.TexturePropertySingleLine(Styles.occlusionText, occlusionMap, (occlusionMap.textureValue != null) ? occlusionStrength : null);

            materialEditor.ShaderProperty(emissionEnabledProp, Styles.emissionEnabled);
            if (material.GetFloat(Property.EmissionEnabled) > 0.5f)
            {
                EditorGUI.indentLevel += 2;
                materialEditor.TexturePropertyWithHDRColor(Styles.emissionMap, emissionMapProp, emissionColorProp, false);
                EditorGUI.indentLevel -= 2;
            }

            materialEditor.TexturePropertySingleLine(Styles.iblDFG, _IBL_DFG);

            if (baseMapProp != null)
                materialEditor.TextureScaleOffsetProperty(baseMapProp);
        }

        private void DoPopup(GUIContent label, MaterialProperty property, string[] options)
        {
            if (property != null)
                materialEditor.PopupShaderProperty(property, label, options);
        }

        private static void DrawFloatToggleProperty(GUIContent styles, MaterialProperty prop, int indentLevel = 0, bool isDisabled = false)
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

            if (material.HasProperty(Property.CullMode))
                material.doubleSidedGI = (RenderFace)material.GetFloat(Property.CullMode) != RenderFace.Front;

            if (material.HasProperty(Property.MetallicGlossMap))
                CoreUtils.SetKeyword(material, ShaderKeywordStrings._METALLICGLOSSMAP, material.GetTexture(Property.MetallicGlossMap));

            if (material.HasProperty(Property.BumpMap))
                CoreUtils.SetKeyword(material, ShaderKeywordStrings._NORMALMAP, material.GetTexture(Property.BumpMap));

            if (material.HasProperty(Property.OcclusionMap))
                CoreUtils.SetKeyword(material, ShaderKeywordStrings._OCCLUSIONMAP, material.GetTexture(Property.OcclusionMap));

            if (material.HasProperty(Property.EmissionColor))
                MaterialEditor.FixupEmissiveFlag(material);

            CoreUtils.SetKeyword(material, ShaderKeywordStrings._EMISSION, material.GetFloat(Property.EmissionEnabled) > 0.5f);
        }

        private static void SetupMaterialBlendModeInternal(Material material)
        {
            if (material == null)
                throw new ArgumentNullException("material");

            int renderQueue = material.shader.renderQueue;
            material.SetOverrideTag("RenderType", "");
            bool castShadows = true;
            if (material.HasProperty(Property.SurfaceType))
            {
                SurfaceType surfaceType = (SurfaceType)material.GetFloat(Property.SurfaceType);
                bool zwrite = false;
                if (surfaceType == SurfaceType.Opaque)
                {
                    renderQueue = (int)RenderQueue.Geometry;
                    material.SetOverrideTag("RenderType", "Opaque");

                    SetMaterialSrcDstBlendProperties(material, UnityEngine.Rendering.BlendMode.One, UnityEngine.Rendering.BlendMode.Zero);
                    zwrite = true;
                }
                else
                {
                    BlendMode blendMode = (BlendMode)material.GetFloat(Property.BlendMode);

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
                    castShadows = false;
                    renderQueue = (int)RenderQueue.Transparent;
                }

                if (material.HasProperty(Property.ZWrite))
                    material.SetFloat(Property.ZWrite, zwrite ? 1.0f : 0.0f);
            }

            if (renderQueue != material.renderQueue)
                material.renderQueue = renderQueue;

            material.SetShaderPassEnabled("ShadowCaster", castShadows);
        }

        private static void SetMaterialSrcDstBlendProperties(Material material, UnityEngine.Rendering.BlendMode srcBlend, UnityEngine.Rendering.BlendMode dstBlend)
        {
            if (material.HasProperty(Property.SrcBlend))
                material.SetFloat(Property.SrcBlend, (float)srcBlend);

            if (material.HasProperty(Property.DstBlend))
                material.SetFloat(Property.DstBlend, (float)dstBlend);

            if (material.HasProperty(Property.SrcBlendAlpha))
                material.SetFloat(Property.SrcBlendAlpha, (float)srcBlend);

            if (material.HasProperty(Property.DstBlendAlpha))
                material.SetFloat(Property.DstBlendAlpha, (float)dstBlend);
        }

        private static void SetMaterialSrcDstBlendProperties(Material material, UnityEngine.Rendering.BlendMode srcBlendRGB, UnityEngine.Rendering.BlendMode dstBlendRGB, UnityEngine.Rendering.BlendMode srcBlendAlpha, UnityEngine.Rendering.BlendMode dstBlendAlpha)
        {
            if (material.HasProperty(Property.SrcBlend))
                material.SetFloat(Property.SrcBlend, (float)srcBlendRGB);

            if (material.HasProperty(Property.DstBlend))
                material.SetFloat(Property.DstBlend, (float)dstBlendRGB);

            if (material.HasProperty(Property.SrcBlendAlpha))
                material.SetFloat(Property.SrcBlendAlpha, (float)srcBlendAlpha);

            if (material.HasProperty(Property.DstBlendAlpha))
                material.SetFloat(Property.DstBlendAlpha, (float)dstBlendAlpha);
        }
    }
}
