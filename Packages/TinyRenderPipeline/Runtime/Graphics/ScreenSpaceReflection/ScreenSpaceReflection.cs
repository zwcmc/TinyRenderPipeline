using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class ScreenSpaceReflection
{
    private static readonly ProfilingSampler s_ScreenSpaceReflectionSampler = new ProfilingSampler("Screen Space Reflection");

    private const string k_SsrHitPointTextureNameWrite = "_SsrHitPointTextureWrite";
    private const string k_SsrHitPointTextureNameRead = "_SsrHitPointTexture";
    private const string k_SsrTexture = "_SsrTexture";

    private static class ScreenSpaceReflectionShaderIDs
    {
        public static int SsrStencilBit = Shader.PropertyToID("_SsrStencilBit");

        public static int SsrHitPointTextureWrite = Shader.PropertyToID(k_SsrHitPointTextureNameWrite);
        public static int SsrHitPointTextureRead = Shader.PropertyToID(k_SsrHitPointTextureNameRead);

        public static int StencilTexture = Shader.PropertyToID("_StencilTexture");
        public static int DepthPyramidTexture = Shader.PropertyToID("_DepthPyramidTexture");
        public static int ScreenSize = Shader.PropertyToID("_ScreenSize");

        public static int InvViewProjection = Shader.PropertyToID("_InvViewProjection");

        public static int HistoryReprojection = Shader.PropertyToID("_HistoryReprojection");
        public static int SsrHistoryColorTexture = Shader.PropertyToID(FrameHistory.s_SsrHistoryColorTextureName);
        public static int SsrTexture = Shader.PropertyToID(k_SsrTexture);
    }

    private class PassData
    {
        public TextureHandle stencilTexture;
        public TextureHandle depthPyramidTexture;
        public TextureHandle hitPointTexture;
        public Material material;
        public ComputeShader cs;
        public int ssrMarchingKernel;
        public int width;
        public int height;

        public Matrix4x4 invViewProjection;

        public Matrix4x4 historyReprojection;
        public int ssrReprojectionKernel;
        public TextureHandle ssrHistoryColorTexture;
        public TextureHandle ssrTexture;
    }

    private ComputeShader m_SsrShader;
    private int m_SsrMarchingKernel;
    private int m_SsrReprojectionKernel;

    private DepthPyramidGenerator m_DepthPyramidGenerator;
    private ColorPyramidGenerator m_ColorPyramidGenerator;

    public ScreenSpaceReflection(ComputeShader ssrShader, ComputeShader depthPyramidShader, ComputeShader colorPyramidShader)
    {
        m_SsrShader = ssrShader;

        m_SsrMarchingKernel = m_SsrShader.FindKernel("ScreenSpaceReflectionMarching");
        m_SsrReprojectionKernel = m_SsrShader.FindKernel("ScreenSpaceReflectionReprojection");

        m_DepthPyramidGenerator = new DepthPyramidGenerator(depthPyramidShader);
        m_ColorPyramidGenerator = new ColorPyramidGenerator(colorPyramidShader);
    }

    public void RecordRenderGraph(RenderGraph renderGraph, in TextureHandle depthStencilTexture, ref RenderingData renderingData)
    {
        // Generate depth pyramid
        TextureHandle depthPyramidTexture;
        m_DepthPyramidGenerator.RenderMinDepthPyramid(renderGraph, in depthStencilTexture, out depthPyramidTexture, ref renderingData);

        TextureHandle _SsrHitPointTexture;
        var descriptor = renderingData.cameraData.targetDescriptor;
        descriptor.graphicsFormat = GraphicsFormat.R16G16_UNorm;
        descriptor.depthStencilFormat = GraphicsFormat.None;
        descriptor.enableRandomWrite = true;
        _SsrHitPointTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, descriptor, k_SsrHitPointTextureNameRead, false, FilterMode.Bilinear);

        TextureHandle _SsrTexture;
        descriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
        descriptor.mipCount = 9;
        descriptor.useMipMap = true;
        _SsrTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, descriptor, k_SsrTexture, false, FilterMode.Trilinear);

        using (var builder = renderGraph.AddComputePass<PassData>(s_ScreenSpaceReflectionSampler.name, out var passData, s_ScreenSpaceReflectionSampler))
        {
            passData.cs = m_SsrShader;
            passData.ssrMarchingKernel = m_SsrMarchingKernel;
            passData.ssrReprojectionKernel = m_SsrReprojectionKernel;
            passData.width = descriptor.width;
            passData.height = descriptor.height;

            Matrix4x4 gpuProjectionMatirx = GL.GetGPUProjectionMatrix(FrameHistory.GetCurrentFrameProjection(), true);
            Matrix4x4 gpuViewMatrix = FrameHistory.GetCurrentFrameView();
            Matrix4x4 currentFrameGpuVP = gpuProjectionMatirx * gpuViewMatrix;

            passData.invViewProjection = Matrix4x4.Inverse(currentFrameGpuVP);

            Matrix4x4 historyViewProjection = GL.GetGPUProjectionMatrix(FrameHistory.GetLastFrameProjection(), true) * FrameHistory.GetLastFrameView();
            Matrix4x4 normalizedToClip = Matrix4x4.identity;
            normalizedToClip.m00 = 2.0f;
            normalizedToClip.m03 = -1.0f;
            normalizedToClip.m11 = 2.0f;
            normalizedToClip.m13 = -1.0f;
            passData.historyReprojection = historyViewProjection * Matrix4x4.Inverse(currentFrameGpuVP) * normalizedToClip;

            passData.stencilTexture = builder.UseTexture(depthStencilTexture, IBaseRenderGraphBuilder.AccessFlags.Read);
            passData.depthPyramidTexture = builder.UseTexture(depthPyramidTexture, IBaseRenderGraphBuilder.AccessFlags.Read);

            passData.hitPointTexture = builder.UseTexture(_SsrHitPointTexture, IBaseRenderGraphBuilder.AccessFlags.ReadWrite);

            passData.ssrHistoryColorTexture = builder.UseTexture((FrameHistory.s_SsrHistoryColorRT == null || FrameHistory.s_SsrHistoryColorRT.rt == null) ? renderGraph.defaultResources.blackTexture : renderGraph.ImportTexture(FrameHistory.s_SsrHistoryColorRT), IBaseRenderGraphBuilder.AccessFlags.Read);
            passData.ssrTexture = builder.UseTexture(_SsrTexture, IBaseRenderGraphBuilder.AccessFlags.WriteAll);

            builder.SetRenderFunc((PassData data, ComputeGraphContext context) =>
            {
                var cmd = context.cmd;

                // SSR Marching
                cmd.SetComputeIntParam(data.cs, ScreenSpaceReflectionShaderIDs.SsrStencilBit, (int)StencilUsage.ScreenSpaceReflection);

                cmd.SetComputeTextureParam(data.cs, data.ssrMarchingKernel, ScreenSpaceReflectionShaderIDs.StencilTexture, data.stencilTexture, 0, RenderTextureSubElement.Stencil);
                cmd.SetComputeTextureParam(data.cs, data.ssrMarchingKernel, ScreenSpaceReflectionShaderIDs.DepthPyramidTexture, data.depthPyramidTexture);
                cmd.SetComputeTextureParam(data.cs, data.ssrMarchingKernel, ScreenSpaceReflectionShaderIDs.SsrHitPointTextureWrite, data.hitPointTexture);

                cmd.SetComputeVectorParam(data.cs, ScreenSpaceReflectionShaderIDs.ScreenSize, new Vector4((float)data.width, (float)data.height, 1.0f / data.width, 1.0f / data.height));
                cmd.SetComputeMatrixParam(data.cs, ScreenSpaceReflectionShaderIDs.InvViewProjection, data.invViewProjection);

                cmd.DispatchCompute(data.cs, data.ssrMarchingKernel, CommonUtils.DivRoundUp(data.width, 8), CommonUtils.DivRoundUp(data.height, 8), 1);

                // SSR Color Texture
                cmd.SetComputeMatrixParam(data.cs, ScreenSpaceReflectionShaderIDs.HistoryReprojection, data.historyReprojection);
                cmd.SetComputeTextureParam(data.cs, data.ssrReprojectionKernel, ScreenSpaceReflectionShaderIDs.DepthPyramidTexture, data.depthPyramidTexture);
                cmd.SetComputeTextureParam(data.cs, data.ssrReprojectionKernel, ScreenSpaceReflectionShaderIDs.SsrHistoryColorTexture, data.ssrHistoryColorTexture);
                cmd.SetComputeTextureParam(data.cs, data.ssrReprojectionKernel, ScreenSpaceReflectionShaderIDs.SsrHitPointTextureRead, data.hitPointTexture);
                cmd.SetComputeTextureParam(data.cs, data.ssrReprojectionKernel, ScreenSpaceReflectionShaderIDs.SsrTexture, data.ssrTexture);
                cmd.DispatchCompute(data.cs, data.ssrReprojectionKernel, CommonUtils.DivRoundUp(data.width, 8), CommonUtils.DivRoundUp(data.height, 8), 1);
            });
        }

        m_ColorPyramidGenerator.RenderColorPyramid(renderGraph, ref _SsrTexture, in descriptor, ref renderingData);

        RenderingUtils.SetGlobalRenderGraphTextureName(renderGraph, k_SsrTexture, _SsrTexture, "Set Global SSR Texture");
    }
}
