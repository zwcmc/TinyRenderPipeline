using UnityEngine;

[CreateAssetMenu(menuName = "Rendering/Post Processing Settings")]
public class PostProcessingSettings : ScriptableObject
{
    [SerializeField]
    private Shader shader = default;

    public Shader postProcessingShader => shader;
}
