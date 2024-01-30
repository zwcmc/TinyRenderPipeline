using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class RenderingUtils
{
    private static List<ShaderTagId> m_TinyRPShaderTagIds = new List<ShaderTagId>
    {
        new ShaderTagId("TinyRPUnlit")
    };

    public static DrawingSettings CreateDrawingSettings(ref RenderingData renderingData, SortingCriteria sortingCriteria)
    {
        Camera camera = renderingData.camera;
        SortingSettings sortingSettings = new SortingSettings(camera) { criteria = sortingCriteria };
        DrawingSettings settings = new DrawingSettings(m_TinyRPShaderTagIds[0], sortingSettings)
        {
            enableDynamicBatching = false,
            enableInstancing = true
        };

        for (int i = 1; i < m_TinyRPShaderTagIds.Count; ++i)
            settings.SetShaderPassName(i, m_TinyRPShaderTagIds[i]);

        return settings;
    }
}
