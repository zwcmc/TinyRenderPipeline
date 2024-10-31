using UnityEditor;

[CustomEditor(typeof(PostProcessingData), true)]
public class PostProcessingDataEditor : Editor
{
    private SerializedProperty m_Shaders;

    private SerializedProperty m_Bloom;
    private SerializedProperty m_Tonemapping;
    private SerializedProperty m_ColorAdjustments;
    private SerializedProperty m_WhiteBalance;

    private void OnEnable()
    {
        m_Shaders = serializedObject.FindProperty("shaders");

        m_Bloom = serializedObject.FindProperty("bloom");
        m_Tonemapping = serializedObject.FindProperty("tonemapping");
        m_ColorAdjustments = serializedObject.FindProperty("colorAdjustments");
        m_WhiteBalance = serializedObject.FindProperty("whiteBalance");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        if (EditorPrefs.GetBool("DeveloperMode"))
            EditorGUILayout.PropertyField(m_Shaders);

        EditorGUILayout.PropertyField(m_Bloom);
        EditorGUILayout.PropertyField(m_Tonemapping);
        EditorGUILayout.PropertyField(m_ColorAdjustments);
        EditorGUILayout.PropertyField(m_WhiteBalance);

        serializedObject.ApplyModifiedProperties();
    }
}
