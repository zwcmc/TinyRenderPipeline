using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class CopyColorPass
{
    private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler("Copy Color");

    private static class CopyColorShaderIDs
    {
        public static readonly int CameraColorAttachment = Shader.PropertyToID("_CameraColorAttachment");
        public static readonly int CameraColorTexture = Shader.PropertyToID("_CameraColorTexture");
    }

    private ComputeShader m_Shader;

    private class ComputePassData
    {
        public ComputeShader shader;
        public int kernelID;
        public TextureHandle sourceColorTexture;
        public TextureHandle destColorTexture;
        public Vector2Int size;
    }

    public CopyColorPass(ComputeShader shader)
    {
        m_Shader = shader;
    }

    public void RecordRenderGraphCompute(RenderGraph renderGraph, in TextureHandle sourceColorTexture, out TextureHandle cameraColorTexture, ref RenderingData renderingData)
    {
        var colorDescriptor = renderingData.cameraTargetDescriptor;
        colorDescriptor.depthBufferBits = (int)DepthBits.None;
        colorDescriptor.depthStencilFormat = GraphicsFormat.None;
        colorDescriptor.enableRandomWrite = true;
        cameraColorTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, colorDescriptor, "_CameraColorTexture", false, FilterMode.Bilinear);

        using (var builder = renderGraph.AddComputePass<ComputePassData>(s_ProfilingSampler.name, out var passData, s_ProfilingSampler))
        {
            passData.shader = m_Shader;
            passData.kernelID = m_Shader.FindKernel("CSCopyColor");
            passData.sourceColorTexture = sourceColorTexture;
            passData.destColorTexture = cameraColorTexture;
            passData.size = new Vector2Int(renderingData.cameraTargetDescriptor.width, renderingData.cameraTargetDescriptor.height);

            builder.UseTexture(sourceColorTexture, IBaseRenderGraphBuilder.AccessFlags.Read);
            builder.UseTexture(cameraColorTexture, IBaseRenderGraphBuilder.AccessFlags.WriteAll);

            builder.SetRenderFunc((ComputePassData data, ComputeGraphContext context) =>
            {
                var cmd = context.cmd;

                cmd.SetComputeTextureParam(data.shader, data.kernelID, CopyColorShaderIDs.CameraColorAttachment, data.sourceColorTexture);
                cmd.SetComputeTextureParam(data.shader, data.kernelID, CopyColorShaderIDs.CameraColorTexture, data.destColorTexture);

                int dispatchSizeX = Utils.DivRoundUp(data.size.x, 8);
                int dispatchSizeY = Utils.DivRoundUp(data.size.y, 8);

                cmd.DispatchCompute(data.shader, data.kernelID, dispatchSizeX, dispatchSizeY, 1);
            });
        }

        // Set global camera color texture
        RenderingUtils.SetGlobalRenderGraphTextureID(renderGraph, CopyColorShaderIDs.CameraColorTexture, cameraColorTexture, "SetGlobalCameraColorTexture");
    }
}
