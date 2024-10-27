using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class FinalBlitPass
{
    private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler("FinalBlit");

    private RTHandle m_Source;
    private Material m_BlitMaterial;

    private class PassData
    {
        public TextureHandle source;
        public TextureHandle destination;
        public RenderingData renderingData;
        public Material blitMaterial;
    }

    public FinalBlitPass(Material blitMaterial)
    {
        m_BlitMaterial = blitMaterial;
    }

    public void Record(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, ref RenderingData renderingData)
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
}
