using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TinyRenderPipelineAsset))]
public class TinyRenderPipelineAssetEditor : Editor
{
    private SerializedProperty m_Shadows;
    private SerializedProperty m_PostProcessingData;
    private SerializedProperty m_RenderScale;
    private SerializedProperty m_AntialiasingMode;
    private SerializedProperty m_SSAO;

    private void OnEnable()
    {
        m_Shadows = serializedObject.FindProperty("m_Shadows");
        m_PostProcessingData = serializedObject.FindProperty("postProcessingData");
        m_RenderScale = serializedObject.FindProperty("renderScale");
        m_AntialiasingMode = serializedObject.FindProperty("antialiasingMode");
        m_SSAO = serializedObject.FindProperty("ssaoEnabled");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(m_Shadows);

        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(m_PostProcessingData);

        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(m_RenderScale);

        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(m_AntialiasingMode);

        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(m_SSAO, new GUIContent("SSAO"));

        serializedObject.ApplyModifiedProperties();
    }
}
