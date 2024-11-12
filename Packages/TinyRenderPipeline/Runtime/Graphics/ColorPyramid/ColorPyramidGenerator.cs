using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class ColorPyramidGenerator
{
    private static readonly ProfilingSampler s_ColorPyramidSampler = new ProfilingSampler("Color Pyramid");

    private const int k_MipCount = 9;

    private static class ColorPyramidShaderIDs
    {
        public static int PrevPyramidColor = Shader.PropertyToID("_PrevPyramidColor");
        public static int CurrPyramidColor = Shader.PropertyToID("_CurrPyramidColor");
        public static int CurrPyramidSize = Shader.PropertyToID("_CurrPyramidSize");
    }

    private ComputeShader m_Shader;
    private int m_ColorPyramidKernel;

    private Vector2Int[] m_ColorPyramidSizes;
    private TextureHandle[] m_ColorPyramidTextures;

    private class PassData
    {
        public ComputeShader cs;
        public int kernel;
        public int mipCount;
        public TextureHandle[] colorPyramidTextures;
        public Vector2Int[] colorPyramidSizes;
        public CommandBuffer commandBuffer;
        public TextureHandle sourceColorTexture;
    }

    public ColorPyramidGenerator(ComputeShader shader)
    {
        m_Shader = shader;
        m_ColorPyramidKernel = m_Shader.FindKernel("CSMain");

        m_ColorPyramidSizes = new Vector2Int[k_MipCount - 1];
        m_ColorPyramidTextures = new TextureHandle[k_MipCount - 1];
    }

    public void RenderColorPyramid(RenderGraph renderGraph, ref TextureHandle colorPyramidTexture, in RenderTextureDescriptor colorPyramidDescriptor, ref RenderingData renderingData)
    {
        var descriptor = colorPyramidDescriptor;
        descriptor.mipCount = 1;
        descriptor.useMipMap = false;

        Vector2Int pyramid0Size = new Vector2Int(descriptor.width, descriptor.height);
        for (int i = 0; i < k_MipCount - 1; ++i)
        {
            pyramid0Size.x >>= 1;
            pyramid0Size.y >>= 1;

            descriptor.width = pyramid0Size.x;
            descriptor.height = pyramid0Size.y;

            m_ColorPyramidSizes[i] = pyramid0Size;
            m_ColorPyramidTextures[i] = RenderingUtils.CreateRenderGraphTexture(renderGraph, descriptor, "_ColorPyramid" + i, false, FilterMode.Bilinear, TextureWrapMode.Clamp);
        }

        using (var builder = renderGraph.AddComputePass<PassData>(s_ColorPyramidSampler.name, out var passData, s_ColorPyramidSampler))
        {
            passData.cs = m_Shader;
            passData.kernel = m_ColorPyramidKernel;
            passData.mipCount = k_MipCount;
            passData.colorPyramidTextures = m_ColorPyramidTextures;
            passData.colorPyramidSizes = m_ColorPyramidSizes;
            passData.commandBuffer = renderingData.commandBuffer;

            passData.sourceColorTexture = builder.UseTexture(colorPyramidTexture, IBaseRenderGraphBuilder.AccessFlags.ReadWrite);
            foreach (var pyramidTexture in m_ColorPyramidTextures)
            {
                builder.UseTexture(pyramidTexture, IBaseRenderGraphBuilder.AccessFlags.ReadWrite);
            }

            builder.AllowPassCulling(false);

            builder.SetRenderFunc((PassData data, ComputeGraphContext context) =>
            {
                var cmd = context.cmd;
                TextureHandle lastPyramidTexture = data.sourceColorTexture;
                for (int i = 0; i < data.mipCount - 1; ++i)
                {
                    var targetPyramidSize = data.colorPyramidSizes[i];

                    int dispatchSizeX = CommonUtils.DivRoundUp(targetPyramidSize.x, 8);
                    int dispatchSizeY = CommonUtils.DivRoundUp(targetPyramidSize.y, 8);

                    if (dispatchSizeX < 1 || dispatchSizeY < 1) break;

                    cmd.SetComputeTextureParam(data.cs, data.kernel, ColorPyramidShaderIDs.PrevPyramidColor, lastPyramidTexture);
                    cmd.SetComputeTextureParam(data.cs, data.kernel, ColorPyramidShaderIDs.CurrPyramidColor, data.colorPyramidTextures[i]);
                    cmd.SetComputeVectorParam(m_Shader, ColorPyramidShaderIDs.CurrPyramidSize, new Vector4(targetPyramidSize.x, targetPyramidSize.y, 1.0f / targetPyramidSize.x, 1.0f / targetPyramidSize.y));
                    cmd.DispatchCompute(data.cs, data.kernel, dispatchSizeX, dispatchSizeY, 1);

                    // Copy texture to target mipmap level
                    data.commandBuffer.CopyTexture(data.colorPyramidTextures[i], 0, 0, data.sourceColorTexture, 0, i + 1);

                    lastPyramidTexture = data.colorPyramidTextures[i];
                }
            });
        }
    }
}
