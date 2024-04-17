using UnityEditor;

[CustomEditor(typeof(AdditionalCameraData), true)]
public class AdditionalCameraDataEditor : Editor
{
    private SerializedProperty m_OverridePostProcessingData;
    private SerializedProperty m_PostProcessingData;
    private SerializedProperty m_RequireDepthTexture;

    private void OnEnable()
    {
        m_OverridePostProcessingData = serializedObject.FindProperty("m_OverridePostProcessingData");
        m_PostProcessingData = serializedObject.FindProperty("m_PostProcessingData");
        m_RequireDepthTexture = serializedObject.FindProperty("m_RequireDepthTexture");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(m_OverridePostProcessingData, true);
        if (m_OverridePostProcessingData.boolValue)
        {
            EditorGUILayout.PropertyField(m_PostProcessingData, true);
        }

        EditorGUILayout.PropertyField(m_RequireDepthTexture, true);

        serializedObject.ApplyModifiedProperties();
    }
}
