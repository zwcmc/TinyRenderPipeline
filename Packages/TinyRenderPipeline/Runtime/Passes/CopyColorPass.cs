using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class CopyColorPass
{
    private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler("CopyColor");

    private RTHandle m_Source;
    private RTHandle m_Destination;
    private Material m_CopyColorMaterial;

    private class PassData
    {
        public TextureHandle source;
        public Material blitMaterial;
    }

    private PassData m_PassData;

    public CopyColorPass(Material copyColorMaterial)
    {
        m_CopyColorMaterial = copyColorMaterial;
        m_PassData = new PassData();
    }

    public void Render(ScriptableRenderContext context, RTHandle source, RTHandle destination, ref RenderingData renderingData)
    {
        m_Source = source;
        m_Destination = destination;

        var cmd = renderingData.commandBuffer;
        using (new ProfilingScope(cmd, s_ProfilingSampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CoreUtils.SetRenderTarget(cmd, m_Destination, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, ClearFlag.None, Color.black);

            m_PassData.blitMaterial = m_CopyColorMaterial;

            ExecutePass(CommandBufferHelpers.GetRasterCommandBuffer(cmd), ref m_PassData, m_Source);
        }
    }

    public void Record(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ref RenderingData renderingData)
    {
        // Copy color pass
        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_ProfilingSampler.name, out var passData, s_ProfilingSampler))
        {
            passData.source = source;
            passData.blitMaterial = m_CopyColorMaterial;

            builder.UseTexture(source, IBaseRenderGraphBuilder.AccessFlags.Read);
            builder.UseTextureFragment(destination, 0, IBaseRenderGraphBuilder.AccessFlags.Write);

            builder.AllowPassCulling(false);

            builder.SetRenderFunc((PassData data, RasterGraphContext rasterGraphContext) =>
            {
                ExecutePass(rasterGraphContext.cmd, ref data, data.source);
            });
        }

        // Set global opaque texture
        RenderingUtils.SetGlobalRenderGraphTextureName(renderGraph, "_CameraOpaqueTexture", destination, "SetGlobalCameraOpaqueTexture");
    }

    private static void ExecutePass(RasterCommandBuffer cmd, ref PassData data, RTHandle source)
    {
        var copyColorMaterial = data.blitMaterial;

        if (copyColorMaterial == null)
        {
            Debug.LogError("Copy Color Pass: Copy Color Material is null.");
            return;
        }

        Vector4 scaleBias = new Vector4(1f, 1f, 0f, 0f);
        Blitter.BlitTexture(cmd, source, scaleBias, copyColorMaterial, 0);
    }
}
