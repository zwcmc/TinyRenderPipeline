using UnityEngine;
using UnityEngine.Rendering;

public class CopyColorPass
{
    private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler("CopyColor");

    private RTHandle m_Source;
    private RTHandle m_Destination;

    private Material m_CopyColorMaterial;

    public CopyColorPass(Material copyColorMaterial)
    {
        m_CopyColorMaterial = copyColorMaterial;
    }

    public void Setup(RTHandle source, RTHandle destination)
    {
        m_Source = source;
        m_Destination = destination;
    }

    public void Render(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (m_CopyColorMaterial == null)
        {
            Debug.LogError("Copy Color Pass: Copy Color Material is null.");
            return;
        }

        var cmd = renderingData.commandBuffer;
        using (new ProfilingScope(cmd, s_ProfilingSampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CoreUtils.SetRenderTarget(cmd, m_Destination, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, ClearFlag.None, Color.black);

            Vector4 scaleBias = new Vector4(1, 1, 0, 0);
            Blitter.BlitTexture(cmd, m_Source, scaleBias, m_CopyColorMaterial, 0);
        }
    }
}
