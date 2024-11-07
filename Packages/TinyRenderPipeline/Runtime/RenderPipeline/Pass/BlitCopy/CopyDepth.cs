using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class CopyDepth
{
    private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler("Copy Depth");

    private static class CopyDepthShaderIDs
    {
        public static int CameraDepthAttachment = Shader.PropertyToID("_CameraDepthAttachment");
        public static int CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
    }

    private RTHandle m_Source;
    private RTHandle m_Destination;
    private Material m_CopyDepthMaterial;
    private bool m_CopyToDepthTexture;

    private class PassData
    {
        public TextureHandle source;
        public TextureHandle destination;
        public Material copyDepthMaterial;
        public bool copyToDepth;
        public RenderingData renderingData;
    }

    private ComputeShader m_Shader;

    public CopyDepth(Material copyDepthMaterial, ComputeShader shaderCS, bool copyToDepthTexture = false)
    {
        m_CopyDepthMaterial = copyDepthMaterial;
        m_CopyToDepthTexture = copyToDepthTexture;

        m_Shader = shaderCS;
    }

    private class ComputePassData
    {
        public ComputeShader shader;
        public int kernelID;
        public TextureHandle sourceDepthTexture;
        public TextureHandle destDepthTexture;
        public Vector2Int size;
    }

    public void RecordRenderGraphCompute(RenderGraph renderGraph, in TextureHandle sourceDepthTexture, out TextureHandle targetDepthTexture, ref RenderingData renderingData)
    {
        var depthDescriptor = renderingData.cameraTargetDescriptor;
        depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
        depthDescriptor.depthStencilFormat = GraphicsFormat.None;
        depthDescriptor.enableRandomWrite = true;
        targetDepthTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, depthDescriptor, "_CameraDepthTexture", false, FilterMode.Point, TextureWrapMode.Clamp);

        using (var builder = renderGraph.AddComputePass<ComputePassData>(s_ProfilingSampler.name, out var passData, s_ProfilingSampler))
        {
            passData.shader = m_Shader;
            passData.kernelID = m_Shader.FindKernel("CSCopyDepth");
            passData.sourceDepthTexture = sourceDepthTexture;
            passData.destDepthTexture = targetDepthTexture;
            passData.size = new Vector2Int(renderingData.cameraTargetDescriptor.width, renderingData.cameraTargetDescriptor.height);

            builder.UseTexture(sourceDepthTexture, IBaseRenderGraphBuilder.AccessFlags.Read);
            builder.UseTexture(targetDepthTexture, IBaseRenderGraphBuilder.AccessFlags.WriteAll);

            builder.SetRenderFunc((ComputePassData data, ComputeGraphContext context) =>
            {
                var cmd = context.cmd;
                // int kernel = data.shader.FindKernel("CSCopyDepth");

                cmd.SetComputeTextureParam(data.shader, data.kernelID, CopyDepthShaderIDs.CameraDepthAttachment, data.sourceDepthTexture);
                cmd.SetComputeTextureParam(data.shader, data.kernelID, CopyDepthShaderIDs.CameraDepthTexture, data.destDepthTexture);

                int dispatchSizeX = CommonUtils.DivRoundUp(data.size.x, 8);
                int dispatchSizeY = CommonUtils.DivRoundUp(data.size.y, 8);

                cmd.DispatchCompute(data.shader, data.kernelID, dispatchSizeX, dispatchSizeY, 1);
            });
        }

        RenderingUtils.SetGlobalRenderGraphTextureID(renderGraph, CopyDepthShaderIDs.CameraDepthTexture, targetDepthTexture, "SetGlobalCameraDepthTexture");
    }

    public void RecordRenderGraph(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, TextureHandle activeColorTexture, ref RenderingData renderingData, bool bindAsCameraDepth = false, string passName = "CopyDepthPass")
    {
        if (m_CopyDepthMaterial == null)
        {
            Debug.LogError("Copy Depth Pass: Copy Depth Material is null.");
            return;
        }

        // Set global depth attachment
        RenderingUtils.SetGlobalRenderGraphTextureID(renderGraph, CopyDepthShaderIDs.CameraDepthAttachment, source, "SetGlobalCameraDepthAttachment");

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
            RenderingUtils.SetGlobalRenderGraphTextureID(renderGraph, CopyDepthShaderIDs.CameraDepthTexture, destination, "SetGlobalCameraDepthTexture");
    }

    private static void ExecutePass(RasterCommandBuffer cmd, ref PassData data, RTHandle source, RTHandle destination)
    {
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

        Blitter.BlitTexture(cmd, source, scaleBias, data.copyDepthMaterial, 0);
    }
}
