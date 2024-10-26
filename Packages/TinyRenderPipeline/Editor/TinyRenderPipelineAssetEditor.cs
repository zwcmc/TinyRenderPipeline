using UnityEditor;
using UnityEditor.Rendering;

[CustomEditor(typeof(TinyRenderPipelineAsset))]
public class TinyRenderPipelineAssetEditor : Editor
{
    private SerializedProperty m_Shaders;

    private SerializedProperty m_Shadows;
    private SerializedProperty m_PostProcessingData;
    private SerializedProperty m_ColorGradingLutSize;
    private SerializedProperty m_RequireDepthTexture;
    private SerializedProperty m_RequireColorTexture;
    private SerializedProperty m_RenderScale;
    private SerializedProperty m_UseRenderGraph;

    private void OnEnable()
    {
        m_Shaders = serializedObject.FindProperty("m_Shaders");

        m_Shadows = serializedObject.FindProperty("m_Shadows");
        m_PostProcessingData = serializedObject.FindProperty("m_PostProcessingData");
        m_ColorGradingLutSize = serializedObject.FindProperty("m_ColorGradingLutSize");
        m_RequireDepthTexture = serializedObject.FindProperty("m_RequireDepthTexture");
        m_RequireColorTexture = serializedObject.FindProperty("m_RequireColorTexture");
        m_RenderScale = serializedObject.FindProperty("m_RenderScale");
        m_UseRenderGraph = serializedObject.FindProperty("m_UseRenderGraph");
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
        EditorGUILayout.PropertyField(m_Shadows);
        EditorGUILayout.PropertyField(m_PostProcessingData);
        EditorGUILayout.PropertyField(m_ColorGradingLutSize);
        EditorGUILayout.PropertyField(m_RenderScale);
        EditorGUILayout.PropertyField(m_UseRenderGraph);

        serializedObject.ApplyModifiedProperties();
    }
}
