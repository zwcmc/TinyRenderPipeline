using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class DepthPyramidGenerator
{
    private static readonly ProfilingSampler s_MipmapDepthSampler = new ProfilingSampler("Mipmap Depth");

    private static class MipmapDepthShaderIDs
    {
        public static int PrevMipDepth = Shader.PropertyToID("_PrevMipDepth");
        public static int CurrMipDepth = Shader.PropertyToID("_CurrMipDepth");
        public static int MipmapDepth = Shader.PropertyToID("_MipmapDepthTexture");
    }

    private ComputeShader m_Shader;

    private class PassData
    {
        public ComputeShader cs;
        public int copyKernelID;
        public int kernelID;
        public int mipCount;
        public TextureHandle[] mipMapDepthHandles;
        public Vector2Int[] mipmapDepthSizes;
        public RenderingData renderingData;
        public TextureHandle sourceDepthTexture;
        public TextureHandle destDepthTexture;
    }

    private Vector2Int[] m_MipmapDepthSizes;
    private TextureHandle[] m_MipmapDepthHandles;

    private int m_MipCount = 8;

    private int m_CopyMip0KernelID;
    private int m_MipmapDepthKernelID;

    public DepthPyramidGenerator(ComputeShader shader)
    {
        m_Shader = shader;
        m_CopyMip0KernelID = m_Shader.FindKernel("CSCopyMipmap0Depth");
        m_MipmapDepthKernelID = m_Shader.FindKernel("CSMipmapDepth");

        m_MipmapDepthSizes = new Vector2Int[m_MipCount];
        m_MipmapDepthHandles = new TextureHandle[m_MipCount];
    }

    public void RecordRenderGraphCompute(RenderGraph renderGraph, in TextureHandle depthTexture, out TextureHandle mipmapDepthTexture, ref RenderingData renderingData)
    {
        var mipmapDepthDescriptor = renderingData.cameraData.targetDescriptor;
        mipmapDepthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
        mipmapDepthDescriptor.depthStencilFormat = GraphicsFormat.None;
        mipmapDepthDescriptor.mipCount = 8;
        mipmapDepthDescriptor.useMipMap = true;
        mipmapDepthDescriptor.enableRandomWrite = true;
        mipmapDepthTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, mipmapDepthDescriptor, "_MipmapDepthTexture", false, FilterMode.Point, TextureWrapMode.Clamp);

        mipmapDepthDescriptor.mipCount = 1;
        mipmapDepthDescriptor.useMipMap = false;

        Vector2Int mipmapSize = new Vector2Int(mipmapDepthDescriptor.width, mipmapDepthDescriptor.height);
        for (int i = 0; i < m_MipCount; ++i)
        {
            mipmapDepthDescriptor.width = mipmapSize.x;
            mipmapDepthDescriptor.height = mipmapSize.y;

            m_MipmapDepthSizes[i] = mipmapSize;
            m_MipmapDepthHandles[i] = RenderingUtils.CreateRenderGraphTexture(renderGraph, mipmapDepthDescriptor, "_MipmapDepth_" + i, false, FilterMode.Point, TextureWrapMode.Clamp);

            mipmapSize.x = Mathf.Max(mipmapSize.x >> 1, 1);
            mipmapSize.y = Mathf.Max(mipmapSize.y >> 1, 1);
        }


        using (var builder = renderGraph.AddComputePass<PassData>(s_MipmapDepthSampler.name, out var passData, s_MipmapDepthSampler))
        {
            passData.cs = m_Shader;
            passData.copyKernelID = m_CopyMip0KernelID;
            passData.kernelID = m_MipmapDepthKernelID;
            passData.mipCount = m_MipCount;
            passData.mipmapDepthSizes = m_MipmapDepthSizes;
            passData.mipMapDepthHandles = m_MipmapDepthHandles;
            passData.renderingData = renderingData;
            passData.sourceDepthTexture = depthTexture;
            passData.destDepthTexture = mipmapDepthTexture;

            builder.UseTexture(depthTexture, IBaseRenderGraphBuilder.AccessFlags.Read);
            for (int i = 0; i < m_MipCount; i++)
            {
                builder.UseTexture(m_MipmapDepthHandles[i], IBaseRenderGraphBuilder.AccessFlags.ReadWrite);
            }

            builder.UseTexture(mipmapDepthTexture, IBaseRenderGraphBuilder.AccessFlags.WriteAll);

            builder.SetRenderFunc((PassData data, ComputeGraphContext context) =>
            {
                var cmd = context.cmd;
                TextureHandle lastMipTexture = data.sourceDepthTexture;
                for (int i = 0; i < data.mipCount; i++)
                {
                    var mipSize = data.mipmapDepthSizes[i];

                    int dispatchSizeX = CommonUtils.DivRoundUp(mipSize.x, 8);
                    int dispatchSizeY = CommonUtils.DivRoundUp(mipSize.y, 8);

                    if (dispatchSizeX < 1 || dispatchSizeY < 1) break;

                    int kernelID = i == 0 ? data.copyKernelID : data.kernelID;

                    cmd.SetComputeTextureParam(data.cs, kernelID, MipmapDepthShaderIDs.PrevMipDepth, lastMipTexture);
                    cmd.SetComputeTextureParam(data.cs, kernelID, MipmapDepthShaderIDs.CurrMipDepth, data.mipMapDepthHandles[i]);
                    cmd.DispatchCompute(data.cs, kernelID, dispatchSizeX, dispatchSizeY, 1);

                    // Copy texture to target mipmap level
                    data.renderingData.commandBuffer.CopyTexture(data.mipMapDepthHandles[i], 0, 0, data.destDepthTexture, 0, i);

                    lastMipTexture = data.mipMapDepthHandles[i];
                }
            });
        }

        RenderingUtils.SetGlobalRenderGraphTextureID(renderGraph, MipmapDepthShaderIDs.MipmapDepth, mipmapDepthTexture, "SetGlobalMipmapDepthTexture");
    }
}
