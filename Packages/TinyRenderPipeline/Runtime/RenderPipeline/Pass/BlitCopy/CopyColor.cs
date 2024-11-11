using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class CopyColor
{
    private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler("Copy Color");
    private static readonly ProfilingSampler s_CopySsrHistorySampler = new ProfilingSampler("Copy SSR History");

    private static class CopyColorShaderIDs
    {
        public static readonly int CameraColorAttachment = Shader.PropertyToID("_CameraColorAttachment");
        public static readonly int CameraColorTexture = Shader.PropertyToID("_CameraColorTexture");
        public static readonly int SsrHistoryColorTexture = Shader.PropertyToID(FrameHistory.s_SsrHistoryColorTextureName);
    }

    private ComputeShader m_Shader;

    private class PassData
    {
        public ComputeShader shader;
        public int copyKernel;
        public TextureHandle sourceColorTexture;
        public TextureHandle destColorTexture;
        public Vector2Int size;
    }

    private int m_CopyColorKernel;

    public CopyColor(ComputeShader shader)
    {
        m_Shader = shader;
        m_CopyColorKernel = m_Shader.FindKernel("CopyColor");
    }

    public void RecordRenderGraphCompute(RenderGraph renderGraph, in TextureHandle sourceColorTexture, out TextureHandle cameraColorTexture, ref RenderingData renderingData)
    {
        var colorDescriptor = renderingData.cameraData.targetDescriptor;
        colorDescriptor.depthBufferBits = (int)DepthBits.None;
        colorDescriptor.depthStencilFormat = GraphicsFormat.None;
        colorDescriptor.enableRandomWrite = true;
        cameraColorTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, colorDescriptor, "_CameraColorTexture", false, FilterMode.Bilinear);

        using (var builder = renderGraph.AddComputePass<PassData>(s_ProfilingSampler.name, out var passData, s_ProfilingSampler))
        {
            passData.shader = m_Shader;
            passData.copyKernel = m_CopyColorKernel;
            passData.sourceColorTexture = builder.UseTexture(sourceColorTexture, IBaseRenderGraphBuilder.AccessFlags.Read);
            passData.destColorTexture = builder.UseTexture(cameraColorTexture, IBaseRenderGraphBuilder.AccessFlags.WriteAll);
            passData.size = new Vector2Int(colorDescriptor.width, colorDescriptor.height);

            builder.SetRenderFunc((PassData data, ComputeGraphContext context) =>
            {
                var cmd = context.cmd;

                cmd.SetComputeTextureParam(data.shader, data.copyKernel, CopyColorShaderIDs.CameraColorAttachment, data.sourceColorTexture);
                cmd.SetComputeTextureParam(data.shader, data.copyKernel, CopyColorShaderIDs.CameraColorTexture, data.destColorTexture);

                int dispatchSizeX = CommonUtils.DivRoundUp(data.size.x, 8);
                int dispatchSizeY = CommonUtils.DivRoundUp(data.size.y, 8);

                cmd.DispatchCompute(data.shader, data.copyKernel, dispatchSizeX, dispatchSizeY, 1);
            });
        }

        // Set global camera color texture
        RenderingUtils.SetGlobalRenderGraphTextureID(renderGraph, CopyColorShaderIDs.CameraColorTexture, cameraColorTexture, "SetGlobalCameraColorTexture");
    }

    public void CopySsrHistory(RenderGraph renderGraph, in TextureHandle sourceColorTexture, ref RTHandle ssrHistoryColorRT, ref RenderingData renderingData)
    {
        var colorDescriptor = renderingData.cameraData.targetDescriptor;
        if (ssrHistoryColorRT == null || ssrHistoryColorRT.rt == null)
        {
            colorDescriptor.depthStencilFormat = GraphicsFormat.None;
            colorDescriptor.enableRandomWrite = true;
            RenderingUtils.ReAllocateIfNeeded(ref ssrHistoryColorRT, colorDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: FrameHistory.s_SsrHistoryColorTextureName);
        }

        TextureHandle ssrHistoryColorTexture = renderGraph.ImportTexture(ssrHistoryColorRT);
        using (var builder = renderGraph.AddComputePass<PassData>(s_CopySsrHistorySampler.name, out var passData, s_CopySsrHistorySampler))
        {
            passData.shader = m_Shader;
            passData.copyKernel = m_CopyColorKernel;
            passData.sourceColorTexture = builder.UseTexture(sourceColorTexture, IBaseRenderGraphBuilder.AccessFlags.Read);
            passData.destColorTexture = builder.UseTexture(ssrHistoryColorTexture, IBaseRenderGraphBuilder.AccessFlags.WriteAll);
            passData.size = new Vector2Int(colorDescriptor.width, colorDescriptor.height);

            builder.SetRenderFunc((PassData data, ComputeGraphContext context) =>
            {
                var cmd = context.cmd;

                cmd.SetComputeTextureParam(data.shader, data.copyKernel, CopyColorShaderIDs.CameraColorAttachment, data.sourceColorTexture);
                cmd.SetComputeTextureParam(data.shader, data.copyKernel, CopyColorShaderIDs.CameraColorTexture, data.destColorTexture);

                int dispatchSizeX = CommonUtils.DivRoundUp(data.size.x, 8);
                int dispatchSizeY = CommonUtils.DivRoundUp(data.size.y, 8);

                cmd.DispatchCompute(data.shader, data.copyKernel, dispatchSizeX, dispatchSizeY, 1);
            });
        }

        RenderingUtils.SetGlobalRenderGraphTextureID(renderGraph, CopyColorShaderIDs.SsrHistoryColorTexture, ssrHistoryColorTexture, "Set Global SSR History Color Texture");
    }
}
