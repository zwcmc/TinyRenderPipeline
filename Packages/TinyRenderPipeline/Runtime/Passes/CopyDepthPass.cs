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

            bool yFlip = RenderingUtils.IsHandleYFlipped(m_Source, camera) != RenderingUtils.IsHandleYFlipped(m_Destination, camera);
            Vector4 scaleBias = yFlip ? new Vector4(1, -1, 0, 1) : new Vector4(1, 1, 0, 0);

            bool isGameViewFinalTarget = camera.cameraType == CameraType.Game && m_Destination.nameID == BuiltinRenderTextureType.CameraTarget;
            if (isGameViewFinalTarget)
                cmd.SetViewport(camera.pixelRect);
            else
                cmd.SetViewport(new Rect(0, 0, renderingData.cameraTargetDescriptor.width, renderingData.cameraTargetDescriptor.height));

            Blitter.BlitTexture(cmd, m_Source, scaleBias, m_CopyDepthMaterial, 0);
        }
    }
}
