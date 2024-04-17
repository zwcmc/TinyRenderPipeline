using UnityEditor;

[CustomEditor(typeof(TinyRenderPipelineAsset), true)]
public class TinyRenderPipelineAssetEditor : Editor
{
    private SerializedProperty m_Shaders;

    private SerializedProperty m_UseSRPBatcher;
    private SerializedProperty m_SupportsHDR;
    private SerializedProperty m_Shadows;
    private SerializedProperty m_PostProcessingData;
    private SerializedProperty m_ColorGradingLutSize;
    private SerializedProperty m_RequireDepthTexture;

    private void OnEnable()
    {
        m_Shaders = serializedObject.FindProperty("m_Shaders");

        m_UseSRPBatcher = serializedObject.FindProperty("m_UseSRPBatcher");
        m_SupportsHDR = serializedObject.FindProperty("m_SupportsHDR");
        m_Shadows = serializedObject.FindProperty("m_Shadows");
        m_PostProcessingData = serializedObject.FindProperty("m_PostProcessingData");
        m_ColorGradingLutSize = serializedObject.FindProperty("m_ColorGradingLutSize");
        m_RequireDepthTexture = serializedObject.FindProperty("m_RequireDepthTexture");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        if (EditorPrefs.GetBool("DeveloperMode"))
        {
            EditorGUILayout.PropertyField(m_Shaders, true);
        }

        EditorGUILayout.PropertyField(m_RequireDepthTexture, EditorGUIUtility.TrTempContent("Copy Depth Texture"));
        EditorGUILayout.PropertyField(m_UseSRPBatcher, EditorGUIUtility.TrTempContent("SRP Batcher"));
        EditorGUILayout.PropertyField(m_SupportsHDR, EditorGUIUtility.TrTempContent("HDR"));
        EditorGUILayout.PropertyField(m_Shadows, true);
        EditorGUILayout.PropertyField(m_PostProcessingData, true);
        EditorGUILayout.PropertyField(m_ColorGradingLutSize);

        serializedObject.ApplyModifiedProperties();
    }
}
