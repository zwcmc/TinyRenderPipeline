using UnityEngine;
using UnityEngine.Rendering;

public class FinalBlitPass
{
    private RTHandle m_Source;
    private Material m_BlitMaterial;

    private static readonly ProfilingSampler m_ProfilingSampler = new ("Final Blit");

    public FinalBlitPass(Material blitMaterial)
    {
        m_BlitMaterial = blitMaterial;
    }

    public void Setup(RTHandle colorHandle)
    {
        m_Source = colorHandle;
    }

    public void ExecutePass(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (m_BlitMaterial == null)
        {
            Debug.LogError("Final Blit: Blit Material is null.");
            return;
        }

        var cmd = renderingData.commandBuffer;

        using (new ProfilingScope(cmd, m_ProfilingSampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CoreUtils.SetRenderTarget(cmd, TinyRenderPipeline.k_CameraTarget, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, ClearFlag.None, Color.clear);

            var camera = renderingData.camera;
            var cameraType = camera.cameraType;
            bool isRenderToBackBufferTarget = cameraType != CameraType.SceneView;

            bool yFlip = isRenderToBackBufferTarget && camera.targetTexture == null && SystemInfo.graphicsUVStartsAtTop;
            Vector4 scaleBias = yFlip ? new Vector4(1, -1, 0, 1) : new Vector4(1, 1, 0, 0);
            if (isRenderToBackBufferTarget)
                cmd.SetViewport(camera.pixelRect);

            Blitter.BlitTexture(cmd, m_Source, scaleBias, m_BlitMaterial, 0);
        }
    }

    public void Dispose()
    {

    }
}
