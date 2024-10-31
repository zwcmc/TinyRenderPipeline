using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class MipmapDepth
{
    private static readonly ProfilingSampler s_MipmapDepthSampler = new ProfilingSampler("Mipmap Depth");

    private ComputeShader m_MipmapDepthCS;

    private int m_CopyDepthKernel;
    private int m_GenerateMipmapDepthKernel;

    // private static int m_MipmapDepthID = Shader.PropertyToID("_MipmapDepth");

    private struct MipInfo
    {
        public int mipLevel;
        public Vector2Int paddedResolution;
    }
    private MipInfo[] m_MipInfos;
    private static int k_MaxMipLevels = 8;

    private class PassData
    {
        public int maxMipLevel;
        public MipInfo[] mipInfos;
        public ComputeShader computeShader;
        public int copyDepthKernel;
        public int generateMipmapDepthKernel;
    }

    public MipmapDepth(ComputeShader shaderCS)
    {
        m_MipmapDepthCS = shaderCS;

        m_CopyDepthKernel = m_MipmapDepthCS.FindKernel("CSCopyDepth");
        m_GenerateMipmapDepthKernel = m_MipmapDepthCS.FindKernel("CSMipmapDepth");

        m_MipInfos = new MipInfo[k_MaxMipLevels];
    }

    public void RenderMipmapDepth(RenderGraph renderGraph, in TextureHandle depthTexture, ref RenderingData renderingData)
    {
        TextureHandle mipMapDepthTexture;

        int width = Mathf.Max(32, renderingData.cameraTargetDescriptor.width);
        int height = Mathf.Max(32, renderingData.cameraTargetDescriptor.height);

        float maxSize = Mathf.Max(width, height);
        int maxLevel = (int)Mathf.Log(maxSize, 2.0f) + 1;
        maxLevel = Mathf.Min(maxLevel - 5, k_MaxMipLevels);
        for (int i = 0; i < maxLevel; i++)
        {
            m_MipInfos[i].mipLevel = i;
            m_MipInfos[i].paddedResolution.x = Mathf.Max(width >> i, 1);
            m_MipInfos[i].paddedResolution.y = Mathf.Max(height >> i, 1);
        }

        var descriptor = renderingData.cameraTargetDescriptor;
        descriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
        descriptor.depthStencilFormat = GraphicsFormat.None;
        descriptor.width = width;
        descriptor.height = height;
        descriptor.volumeDepth = maxLevel;
        descriptor.enableRandomWrite = true;
        descriptor.dimension = TextureDimension.Tex2DArray;
        mipMapDepthTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, descriptor, "_MipmapDepthTexture", false, FilterMode.Point, TextureWrapMode.Clamp);

        using (var builder = renderGraph.AddComputePass<PassData>(s_MipmapDepthSampler.name, out var passData, s_MipmapDepthSampler))
        {
            passData.maxMipLevel = maxLevel;
            passData.mipInfos = m_MipInfos;
            passData.computeShader = m_MipmapDepthCS;
            passData.copyDepthKernel = m_CopyDepthKernel;
            passData.generateMipmapDepthKernel = m_GenerateMipmapDepthKernel;

            builder.UseTexture(mipMapDepthTexture, IBaseRenderGraphBuilder.AccessFlags.ReadWrite);

            builder.AllowPassCulling(false);

            builder.SetRenderFunc((PassData data, ComputeGraphContext context) =>
            {
                var cmd = context.cmd;
                cmd.SetComputeTextureParam(data.computeShader, data.copyDepthKernel, "_MipmapDepthTexture", mipMapDepthTexture);
                cmd.DispatchCompute(data.computeShader, data.copyDepthKernel, DivRoundUp(width, 8), DivRoundUp(height, 8), 1);


                for (int i = 1; i < data.maxMipLevel; i++)
                {
                    int sSlice = data.mipInfos[i - 1].mipLevel;
                    int dSlice = data.mipInfos[i].mipLevel;
                    int sW = data.mipInfos[i - 1].paddedResolution.x;
                    int sH = data.mipInfos[i - 1].paddedResolution.y;
                    int dW = data.mipInfos[i].paddedResolution.x;
                    int dH = data.mipInfos[i].paddedResolution.y;
                    cmd.SetComputeTextureParam(data.computeShader, data.generateMipmapDepthKernel, "_MipmapDepthTexture", mipMapDepthTexture);
                    cmd.SetComputeVectorParam(data.computeShader, "sSize", new Vector2(sW, sH));
                    cmd.SetComputeVectorParam(data.computeShader, "dSize", new Vector2(dW, dH));
                    cmd.SetComputeIntParam(data.computeShader, "sSlice", sSlice);
                    cmd.SetComputeIntParam(data.computeShader, "dSlice", dSlice);

                    int xGroup = DivRoundUp(dW, 8);
                    int yGroup = DivRoundUp(dH, 8);
                    cmd.DispatchCompute(data.computeShader, data.generateMipmapDepthKernel, xGroup, yGroup, 1);
                }
            });
        }

        RenderingUtils.SetGlobalRenderGraphTextureName(renderGraph, "_MipmapDepthTexture", mipMapDepthTexture, "Set Global Mipmap Depth");
    }

    private static int DivRoundUp(int x, int y) => (x + y - 1) / y;
}
