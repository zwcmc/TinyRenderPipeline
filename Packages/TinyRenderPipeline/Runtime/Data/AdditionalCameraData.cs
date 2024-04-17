using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class AdditionalCameraData : MonoBehaviour
{
    [SerializeField]
    private bool m_OverridePostProcessingData = false;

    [SerializeField]
    private PostProcessingData m_PostProcessingData = default;

    [SerializeField]
    private bool m_RequireDepthTexture = false;

    public bool isOverridePostProcessingData
    {
        get => m_OverridePostProcessingData;
        set => m_OverridePostProcessingData = value;
    }

    public PostProcessingData overridePostProcessingData => m_PostProcessingData;

    public bool requireDepthTexture
    {
        get => m_RequireDepthTexture;
        set => m_RequireDepthTexture = value;
    }
}
