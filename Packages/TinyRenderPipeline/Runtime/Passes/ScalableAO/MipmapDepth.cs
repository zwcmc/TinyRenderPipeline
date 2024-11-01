using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class MipmapDepth
{
    private static readonly ProfilingSampler s_MipmapDepthSampler = new ProfilingSampler("Compute Mipmap Depth");

    private static class PyramidDepthShaderIDs
    {
        public static int PrevMipDepth = Shader.PropertyToID("_PrevMipDepth");
        public static int CurrMipDepth = Shader.PropertyToID("_CurrMipDepth");
    }

    private ComputeShader m_Shader;

    private class PassData
    {
        public ComputeShader csShader;
        public int mipCount;
        public TextureHandle[] mipMapDepthHandles;
        public Vector2Int[] mipmapDepthSizes;
        public RenderingData renderingData;
    }

    private Vector2Int[] m_MipmapDepthSizes;
    private TextureHandle[] m_MipmapDepthHandles;

    private int m_MipCount = 8;

    public MipmapDepth(ComputeShader shaderCS)
    {
        m_Shader = shaderCS;

        m_MipmapDepthSizes = new Vector2Int[m_MipCount];
        m_MipmapDepthHandles = new TextureHandle[m_MipCount];
    }

    public void RenderMipmapDepth(RenderGraph renderGraph, in TextureHandle depthTexture, ref RenderingData renderingData)
    {
        var pyramidDescriptor = renderingData.cameraTargetDescriptor;
        pyramidDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
        pyramidDescriptor.depthStencilFormat = GraphicsFormat.None;
        pyramidDescriptor.depthBufferBits = (int)DepthBits.None;
        pyramidDescriptor.enableRandomWrite = true;

        Vector2Int pyramidSize = new Vector2Int(pyramidDescriptor.width, pyramidDescriptor.height);
        m_MipmapDepthSizes[0] = pyramidSize;
        m_MipmapDepthHandles[0] = depthTexture;
        for (int i = 1; i < m_MipCount; ++i)
        {
            pyramidSize.x /= 2;
            pyramidSize.y /= 2;

            pyramidDescriptor.width = pyramidSize.x;
            pyramidDescriptor.height = pyramidSize.y;

            m_MipmapDepthSizes[i] = pyramidSize;
            m_MipmapDepthHandles[i] = RenderingUtils.CreateRenderGraphTexture(renderGraph, pyramidDescriptor, "_MipmapDepth_" + i, false, FilterMode.Point, TextureWrapMode.Clamp);
        }


        using (var builder = renderGraph.AddComputePass<PassData>(s_MipmapDepthSampler.name, out var passData, s_MipmapDepthSampler))
        {
            passData.csShader = m_Shader;
            passData.mipCount = m_MipCount;
            passData.mipmapDepthSizes = m_MipmapDepthSizes;
            passData.mipMapDepthHandles = m_MipmapDepthHandles;
            passData.renderingData = renderingData;

            for (int i = 0; i < m_MipCount; i++)
            {
                builder.UseTexture(m_MipmapDepthHandles[i], IBaseRenderGraphBuilder.AccessFlags.ReadWrite);
            }

            builder.SetRenderFunc((PassData data, ComputeGraphContext context) =>
            {
                var cmd = context.cmd;
                TextureHandle lastPyramidTexture = data.mipMapDepthHandles[0];
                for (int i = 1; i < data.mipCount; i++)
                {
                    var mipSize = data.mipmapDepthSizes[i];

                    int dispatchSizeX = DivRoundUp(mipSize.x, 8);
                    int dispatchSizeY = DivRoundUp(mipSize.y, 8);

                    if (dispatchSizeX < 1 || dispatchSizeY < 1) break;

                    int kernel = data.csShader.FindKernel("MipmapDepth");
                    cmd.SetComputeTextureParam(data.csShader, kernel, PyramidDepthShaderIDs.PrevMipDepth, lastPyramidTexture);
                    cmd.SetComputeTextureParam(data.csShader, kernel, PyramidDepthShaderIDs.CurrMipDepth, data.mipMapDepthHandles[i]);
                    cmd.DispatchCompute(data.csShader, kernel, dispatchSizeX, dispatchSizeY, 1);

                    // Copy texture to target mipmap level
                    data.renderingData.commandBuffer.CopyTexture(data.mipMapDepthHandles[i], 0, 0, data.mipMapDepthHandles[0], 0, i);

                    lastPyramidTexture = data.mipMapDepthHandles[i];
                }
            });
        }
    }

    private static int DivRoundUp(int x, int y) => (x + y - 1) / y;
}
