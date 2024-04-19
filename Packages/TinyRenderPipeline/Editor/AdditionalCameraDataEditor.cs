using UnityEditor;
using UnityEditor.Rendering;

[CustomEditor(typeof(AdditionalCameraData), true)]
public class AdditionalCameraDataEditor : Editor
{
    private SerializedProperty m_OverridePostProcessingData;
    private SerializedProperty m_PostProcessingData;
    private SerializedProperty m_RequireDepthTexture;
    private SerializedProperty m_RequireColorTexture;

    private void OnEnable()
    {
        m_OverridePostProcessingData = serializedObject.FindProperty("m_OverridePostProcessingData");
        m_PostProcessingData = serializedObject.FindProperty("m_PostProcessingData");
        m_RequireDepthTexture = serializedObject.FindProperty("m_RequireDepthTexture");
        m_RequireColorTexture = serializedObject.FindProperty("m_RequireColorTexture");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(m_OverridePostProcessingData, EditorGUIUtility.TrTempContent("Override Post Processing"));
        if (m_OverridePostProcessingData.boolValue)
        {
            EditorGUILayout.PropertyField(m_PostProcessingData);
        }

        EditorGUILayout.PropertyField(m_RequireDepthTexture, EditorGUIUtility.TrTempContent("Copy Depth"));
        EditorGUILayout.PropertyField(m_RequireColorTexture, EditorGUIUtility.TrTempContent("Copy Color"));

        serializedObject.ApplyModifiedProperties();
    }
}
