using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class CopyDepthPass
{
    private RTHandle m_Source;
    private RTHandle m_Destination;

    private Material m_CopyDepthMaterial;

    private bool m_CopyToDepthTexture;

    private PassData m_PassData;

    private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler("CopyDepth");

    public CopyDepthPass(Material copyDepthMaterial, bool copyToDepthTexture = false)
    {
        m_CopyDepthMaterial = copyDepthMaterial;
        m_CopyToDepthTexture = copyToDepthTexture;

        m_PassData = new PassData();
    }

    public void Setup(RTHandle source, RTHandle destination)
    {
        m_Source = source;
        m_Destination = destination;
    }

    public void Render(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (m_CopyDepthMaterial == null)
        {
            Debug.LogError("Copy Depth Pass: Copy Depth Material is null.");
            return;
        }

        var cmd = renderingData.commandBuffer;

        cmd.SetGlobalTexture("_CameraDepthAttachment", m_Source.nameID);

        using (new ProfilingScope(cmd, s_ProfilingSampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CoreUtils.SetRenderTarget(cmd, m_Destination, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, ClearFlag.None, Color.black);

            m_PassData.copyDepthMaterial = m_CopyDepthMaterial;
            m_PassData.copyToDepth = m_CopyToDepthTexture;
            m_PassData.renderingData = renderingData;

            ExecutePass(CommandBufferHelpers.GetRasterCommandBuffer(cmd), ref m_PassData, m_Source, m_Destination);
        }
    }

    private class PassData
    {
        public TextureHandle source;
        public TextureHandle destination;
        public Material copyDepthMaterial;
        public bool copyToDepth;
        public RenderingData renderingData;
    }

    private static void ExecutePass(RasterCommandBuffer cmd, ref PassData data, RTHandle source, RTHandle destination)
    {
        var copyDepthMaterial = data.copyDepthMaterial;

        if (copyDepthMaterial == null)
        {
            Debug.LogError("Copy Depth Pass: Copy Depth Material is null.");
            return;
        }

        var copyToDepth = data.copyToDepth;
        if (copyToDepth || destination.rt.graphicsFormat == GraphicsFormat.None)
        {
            cmd.EnableShaderKeyword("_OUTPUT_DEPTH");
        }
        else
        {
            cmd.DisableShaderKeyword("_OUTPUT_DEPTH");
        }

        ref var renderingData = ref data.renderingData;
        var camera = renderingData.camera;
        bool yFlip = RenderingUtils.IsHandleYFlipped(source, camera) != RenderingUtils.IsHandleYFlipped(destination, camera);
        Vector4 scaleBias = yFlip ? new Vector4(1, -1, 0, 1) : new Vector4(1, 1, 0, 0);

        bool isGameViewFinalTarget = camera.cameraType == CameraType.Game && destination.nameID == BuiltinRenderTextureType.CameraTarget;
        if (isGameViewFinalTarget)
            cmd.SetViewport(camera.pixelRect);
        else
            cmd.SetViewport(new Rect(0, 0, renderingData.cameraTargetDescriptor.width, renderingData.cameraTargetDescriptor.height));

        Blitter.BlitTexture(cmd, source, scaleBias, copyDepthMaterial, 0);
    }

    public void RenderGraphRender(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, TextureHandle activeColorTexture, ref RenderingData renderingData, bool bindAsCameraDepth = false, string passName = "CopyDepthPass")
    {
        // Set global depth attachment
        RenderingUtils.SetGlobalRenderGraphTextureName(renderGraph, "_CameraDepthAttachment", source, "SetGlobalCameraDepthAttachment");

        // Copy depth pass
        using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData, s_ProfilingSampler))
        {
            passData.source = builder.UseTexture(source, IBaseRenderGraphBuilder.AccessFlags.Read);

            if (m_CopyToDepthTexture)
            {
                if (activeColorTexture.IsValid())
                    builder.UseTextureFragment(activeColorTexture, 0);
                passData.destination = builder.UseTextureFragmentDepth(destination, IBaseRenderGraphBuilder.AccessFlags.Write);
            }
            else
            {
                passData.destination = builder.UseTextureFragment(destination, 0, IBaseRenderGraphBuilder.AccessFlags.Write);
            }

            passData.copyToDepth = m_CopyToDepthTexture;
            passData.copyDepthMaterial = m_CopyDepthMaterial;
            passData.renderingData = renderingData;

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext rasterGraphContext) =>
            {
                ExecutePass(rasterGraphContext.cmd, ref data, data.source, data.destination);
            });
        }

        // Set global depth texture
        if (bindAsCameraDepth)
            RenderingUtils.SetGlobalRenderGraphTextureName(renderGraph, "_CameraDepthTexture", destination, "SetGlobalCameraDepthTexture");
    }
}
