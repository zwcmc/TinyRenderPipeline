using UnityEngine;

[CreateAssetMenu(menuName = "Rendering/Post Processing Settings")]
public class PostProcessingSettings : ScriptableObject
{
    [SerializeField]
    private Shader shader;

    public Shader postProcessingShader => shader;
}
