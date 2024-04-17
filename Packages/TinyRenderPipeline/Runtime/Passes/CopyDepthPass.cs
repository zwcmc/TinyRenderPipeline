using UnityEngine;
using UnityEngine.Rendering;

public class CopyDepthPass
{
    private RTHandle m_Source;
    private RTHandle m_Destination;

    private Material m_CopyDepthMaterial;

    private static readonly ProfilingSampler m_ProfilingSampler = new ProfilingSampler("Copy Depth");

    public CopyDepthPass(Material copyDepthMaterial)
    {
        m_CopyDepthMaterial = copyDepthMaterial;
    }

    public void Setup(RTHandle source, RTHandle destination)
    {
        m_Source = source;
        m_Destination = destination;
    }

    public void ExecutePass(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (m_CopyDepthMaterial == null)
        {
            Debug.LogError("Copy Depth Pass: Copy Depth Material is null.");
            return;
        }

        var cmd = renderingData.commandBuffer;

        cmd.SetGlobalTexture("_CameraDepthAttachment", m_Source.nameID);

        using (new ProfilingScope(cmd, m_ProfilingSampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CoreUtils.SetRenderTarget(cmd, m_Destination, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, ClearFlag.None, Color.black);

            var camera = renderingData.camera;
            bool isRenderToBackBufferTarget = camera.cameraType != CameraType.SceneView;

            bool yFlip = isRenderToBackBufferTarget && camera.targetTexture == null && SystemInfo.graphicsUVStartsAtTop;
            Vector4 scaleBias = yFlip ? new Vector4(1, -1, 0, 1) : new Vector4(1, 1, 0, 0);
            if (isRenderToBackBufferTarget)
                cmd.SetViewport(camera.pixelRect);

            Blitter.BlitTexture(cmd, m_Source, scaleBias, m_CopyDepthMaterial, 0);
        }
    }
}
