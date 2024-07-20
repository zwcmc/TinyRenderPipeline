using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class FinalBlitPass
{
    private RTHandle m_Source;
    private Material m_BlitMaterial;

    private PassData m_PassData;

    private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler("FinalBlit");

    public FinalBlitPass(Material blitMaterial)
    {
        m_BlitMaterial = blitMaterial;
        m_PassData = new PassData();
    }

    public void Setup(RTHandle colorHandle)
    {
        m_Source = colorHandle;
    }

    public void Render(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (m_BlitMaterial == null)
        {
            Debug.LogError("Final Blit: Blit Material is null.");
            return;
        }

        var cmd = renderingData.commandBuffer;

        using (new ProfilingScope(cmd, s_ProfilingSampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CoreUtils.SetRenderTarget(cmd, TinyRenderPipeline.k_CameraTarget, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, ClearFlag.None, Color.clear);

            m_PassData.blitMaterial = m_BlitMaterial;
            m_PassData.renderingData = renderingData;

            ExecutePass(CommandBufferHelpers.GetRasterCommandBuffer(cmd), ref m_PassData, m_Source);
        }
    }

    private class PassData
    {
        public TextureHandle source;
        public TextureHandle destination;
        public RenderingData renderingData;
        public Material blitMaterial;
    }

    private static void ExecutePass(RasterCommandBuffer cmd, ref PassData data, RTHandle source)
    {
        var blitMaterial = data.blitMaterial;

        if (blitMaterial == null)
        {
            Debug.LogError("Final Blit: Blit Material is null.");
            return;
        }

        ref var renderingData = ref data.renderingData;
        var camera = renderingData.camera;
        var cameraType = camera.cameraType;
        bool isRenderToBackBufferTarget = cameraType != CameraType.SceneView;

        bool yFlip = isRenderToBackBufferTarget && camera.targetTexture == null && SystemInfo.graphicsUVStartsAtTop;
        Vector4 scaleBias = yFlip ? new Vector4(1, -1, 0, 1) : new Vector4(1, 1, 0, 0);
        if (isRenderToBackBufferTarget)
            cmd.SetViewport(camera.pixelRect);

        Blitter.BlitTexture(cmd, source, scaleBias, blitMaterial, 0);
    }

    public void RenderGraphRender(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ref RenderingData renderingData)
    {
        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_ProfilingSampler.name, out var passData, s_ProfilingSampler))
        {
            passData.source = source;
            passData.destination = destination;
            passData.renderingData = renderingData;
            passData.blitMaterial = m_BlitMaterial;

            builder.UseTexture(passData.source, IBaseRenderGraphBuilder.AccessFlags.Read);
            builder.UseTextureFragment(passData.destination, 0, IBaseRenderGraphBuilder.AccessFlags.Write);

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext rasterGraphContext) =>
            {
                ExecutePass(rasterGraphContext.cmd, ref data, data.source);
            });
        }
    }
}
