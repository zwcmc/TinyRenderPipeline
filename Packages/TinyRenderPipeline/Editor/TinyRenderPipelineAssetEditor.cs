using UnityEditor;

[CustomEditor(typeof(TinyRenderPipelineAsset))]
public class TinyRenderPipelineAssetEditor : Editor
{
    private SerializedProperty m_Shadows;
    private SerializedProperty m_PostProcessingData;
    private SerializedProperty m_ColorGradingLutSize;
    private SerializedProperty m_RenderScale;

    private void OnEnable()
    {
        m_Shadows = serializedObject.FindProperty("m_Shadows");
        m_PostProcessingData = serializedObject.FindProperty("m_PostProcessingData");
        m_ColorGradingLutSize = serializedObject.FindProperty("m_ColorGradingLutSize");
        m_RenderScale = serializedObject.FindProperty("m_RenderScale");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(m_Shadows);
        EditorGUILayout.PropertyField(m_PostProcessingData);
        EditorGUILayout.PropertyField(m_ColorGradingLutSize);
        EditorGUILayout.PropertyField(m_RenderScale);

        serializedObject.ApplyModifiedProperties();
    }
}
