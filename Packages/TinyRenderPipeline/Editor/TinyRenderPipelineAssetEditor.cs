using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TinyRenderPipelineAsset))]
public class TinyRenderPipelineAssetEditor : Editor
{
    private SerializedProperty m_Shadows;
    private SerializedProperty m_PostProcessingData;
    private SerializedProperty m_AntialiasingMode;
    private SerializedProperty m_Sao;
    private SerializedProperty m_Ssr;

    private void OnEnable()
    {
        m_Shadows = serializedObject.FindProperty("m_Shadows");
        m_PostProcessingData = serializedObject.FindProperty("postProcessingData");
        m_AntialiasingMode = serializedObject.FindProperty("antialiasingMode");
        m_Sao = serializedObject.FindProperty("saoEnabled");
        m_Ssr = serializedObject.FindProperty("ssrEnabled");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(m_Shadows);

        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(m_PostProcessingData);

        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(m_AntialiasingMode);

        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(m_Sao, new GUIContent("Scalable Ambient Obscurance"));

        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(m_Ssr, new GUIContent("Screen Space Reflection"));

        serializedObject.ApplyModifiedProperties();
    }
}
