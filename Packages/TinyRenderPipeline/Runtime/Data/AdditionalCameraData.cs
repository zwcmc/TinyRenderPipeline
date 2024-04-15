using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class AdditionalCameraData : MonoBehaviour
{
    [SerializeField]
    private bool m_OverridePostProcessingData = false;

    [SerializeField]
    private PostProcessingData m_PostProcessingData = default;

    public bool isOverridePostProcessingData
    {
        get => m_OverridePostProcessingData;
        set => m_OverridePostProcessingData = value;
    }

    public PostProcessingData overridePostProcessingData => m_PostProcessingData;
}
