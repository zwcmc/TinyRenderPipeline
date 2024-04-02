using System;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/Post Processing Data")]
public class PostProcessingData : ScriptableObject
{
    // This controls the size of the bloom texture.
    public enum BloomDownscaleMode
    {
        // Use this to select half size as the starting resolution.
        Half,
        // Use this to select quarter size as the starting resolution.
        Quarter
    }

    [Serializable]
    public class ShaderResources
    {
        public Shader uberPostShader;

        public Shader bloomShader;
    }

    [Serializable]
    public class Bloom
    {
        [Min(0f)]
        public float threshold = 0.9f;

        [Min(0f)]
        public float intensity = 0f;

        [Range(0f, 1f)]
        public float scatter = 0.7f;

        [Min(0f)]
        public float clamp = 65472f;

        [Range(2, 8)]
        public int maxIterations = 6;

        public BloomDownscaleMode downscale = BloomDownscaleMode.Half;

        public bool highQualityFiltering = false;

        public bool IsActive() => intensity > 0f;
    }

    public ShaderResources shaders = default;

    public Bloom bloom = default;
}
