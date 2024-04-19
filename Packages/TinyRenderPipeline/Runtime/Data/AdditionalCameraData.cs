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

    [SerializeField]
    private bool m_RequireColorTexture = false;

    public bool isOverridePostProcessingData
    {
        get { return m_OverridePostProcessingData; }
        set { m_OverridePostProcessingData = value; }
    }

    public PostProcessingData overridePostProcessingData
    {
        get { return m_PostProcessingData; }
    }

    public bool requireDepthTexture
    {
        get { return m_RequireDepthTexture; }
        set { m_RequireDepthTexture = value; }
    }

    public bool requireColorTexture
    {
        get { return m_RequireColorTexture; }
        set { m_RequireColorTexture = value; }
    }
}
