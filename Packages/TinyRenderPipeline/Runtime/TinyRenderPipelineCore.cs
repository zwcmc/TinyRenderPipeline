using UnityEngine;
using UnityEngine.Rendering;

public struct RenderingData
{
    internal ScriptableRenderContext renderContext;
    internal CommandBuffer commandBuffer;
    public Camera camera;
    public CullingResults cullResults;
    public int mainLightIndex;
}
