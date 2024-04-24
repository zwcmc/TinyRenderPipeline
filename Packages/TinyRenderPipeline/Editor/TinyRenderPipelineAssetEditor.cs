using UnityEditor;

[CustomEditor(typeof(TinyRenderPipelineAsset))]
public class TinyRenderPipelineAssetEditor : Editor
{
    private SerializedProperty m_Shaders;

    private SerializedProperty m_UseSRPBatcher;
    private SerializedProperty m_SupportsHDR;
    private SerializedProperty m_Shadows;
    private SerializedProperty m_PostProcessingData;
    private SerializedProperty m_ColorGradingLutSize;
    private SerializedProperty m_RequireDepthTexture;
    private SerializedProperty m_RequireColorTexture;
    private SerializedProperty m_RenderScale;

    private void OnEnable()
    {
        m_Shaders = serializedObject.FindProperty("m_Shaders");

        m_UseSRPBatcher = serializedObject.FindProperty("m_UseSRPBatcher");
        m_SupportsHDR = serializedObject.FindProperty("m_SupportsHDR");
        m_Shadows = serializedObject.FindProperty("m_Shadows");
        m_PostProcessingData = serializedObject.FindProperty("m_PostProcessingData");
        m_ColorGradingLutSize = serializedObject.FindProperty("m_ColorGradingLutSize");
        m_RequireDepthTexture = serializedObject.FindProperty("m_RequireDepthTexture");
        m_RequireColorTexture = serializedObject.FindProperty("m_RequireColorTexture");
        m_RenderScale = serializedObject.FindProperty("m_RenderScale");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        if (EditorPrefs.GetBool("DeveloperMode"))
        {
            EditorGUILayout.PropertyField(m_Shaders);
        }

        EditorGUILayout.PropertyField(m_RequireDepthTexture, EditorGUIUtility.TrTempContent("Copy Depth"));
        EditorGUILayout.PropertyField(m_RequireColorTexture, EditorGUIUtility.TrTempContent("Copy Color"));
        EditorGUILayout.PropertyField(m_UseSRPBatcher, EditorGUIUtility.TrTempContent("SRP Batcher"));
        EditorGUILayout.PropertyField(m_SupportsHDR, EditorGUIUtility.TrTempContent("HDR"));
        EditorGUILayout.PropertyField(m_Shadows);
        EditorGUILayout.PropertyField(m_PostProcessingData);
        EditorGUILayout.PropertyField(m_ColorGradingLutSize);
        EditorGUILayout.PropertyField(m_RenderScale);

        serializedObject.ApplyModifiedProperties();
    }
}
