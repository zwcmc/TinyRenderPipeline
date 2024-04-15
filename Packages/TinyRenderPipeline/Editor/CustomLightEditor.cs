using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

[CanEditMultipleObjects]
[CustomEditor(typeof(Light))]
public class CustomLightEditor : LightEditor
{
    static GUIContent renderingLayerMaskLabel = new GUIContent("Rendering Layer Mask", "");

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        DrawRenderingLayerMask();

        if (!settings.lightType.hasMultipleDifferentValues && (LightType)settings.lightType.enumValueIndex == LightType.Spot)
        {
            settings.DrawInnerAndOuterSpotAngle();
        }

        settings.ApplyModifiedProperties();
    }

    private void DrawRenderingLayerMask()
    {
        SerializedProperty property = settings.renderingLayerMask;

        EditorGUI.showMixedValue = property.hasMultipleDifferentValues;

        EditorGUI.BeginChangeCheck();

        int mask = property.intValue;
        if (mask == int.MaxValue)
            mask = -1;
        mask = EditorGUILayout.MaskField(renderingLayerMaskLabel, mask, GraphicsSettings.currentRenderPipeline.prefixedRenderingLayerMaskNames);

        if (EditorGUI.EndChangeCheck())
        {
            property.intValue = mask == -1 ? int.MaxValue : mask;
        }

        EditorGUI.showMixedValue = false;
    }
}
