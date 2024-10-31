using UnityEditor;

[CustomEditor(typeof(TinyRenderPipelineAsset))]
public class TinyRenderPipelineAssetEditor : Editor
{
    private SerializedProperty m_Shadows;
    private SerializedProperty m_PostProcessingData;
    private SerializedProperty m_RenderScale;
    private SerializedProperty m_AntialiasingMode;

    private void OnEnable()
    {
        m_Shadows = serializedObject.FindProperty("m_Shadows");
        m_PostProcessingData = serializedObject.FindProperty("postProcessingData");
        m_RenderScale = serializedObject.FindProperty("renderScale");
        m_AntialiasingMode = serializedObject.FindProperty("antialiasingMode");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(m_Shadows);
        EditorGUILayout.PropertyField(m_PostProcessingData);
        EditorGUILayout.PropertyField(m_RenderScale);
        EditorGUILayout.PropertyField(m_AntialiasingMode);

        serializedObject.ApplyModifiedProperties();
    }
}
