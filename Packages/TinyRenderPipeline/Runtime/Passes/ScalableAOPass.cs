using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class ScalableAOPass
{
    private static readonly ProfilingSampler s_AOBufferSampler = new("SAO Buffer");
    private static readonly ProfilingSampler s_BilateralBlurSampler = new("SAO Bilateral Blur");
    private static readonly ProfilingSampler s_FinalBilateralBlurSampler = new("SAO Final Bilateral Blur");

    private const string k_SSAOTextureName = "_ScreenSpaceOcclusionTexture";

    Material m_ScalableAOMaterial;

    private static class SAOMaterialParams
    {
        public static readonly int _PositionParams = Shader.PropertyToID("_PositionParams");
        public static readonly int _SAO_Params = Shader.PropertyToID("_SAO_Params");
        public static readonly int _BilateralBlurParams = Shader.PropertyToID("_BilateralBlurParams");
    }

    private class PassData
    {
        public Material saoMaterial;
        public TextureHandle saoBufferTexture;
    }

    public ScalableAOPass()
    {
        m_ScalableAOMaterial = CoreUtils.CreateEngineMaterial("Hidden/Tiny Render Pipeline/ScalableAO");
    }

    public void Record(RenderGraph renderGraph, in TextureHandle depthTexture, out TextureHandle ssaoTexture, ref RenderingData renderingData)
    {
        TextureHandle saoBufferTexture;
        TextureHandle bilateralBlurTexture;

        // Create texture handle
        var saoDescriptor = renderingData.cameraTargetDescriptor;
        saoDescriptor.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
        saoDescriptor.depthStencilFormat = GraphicsFormat.None;

        saoBufferTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, saoDescriptor, "_SAO_Buffer_Texture", false, FilterMode.Bilinear);

        var blurDescriptor = renderingData.cameraTargetDescriptor;
        blurDescriptor.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
        blurDescriptor.depthStencilFormat = GraphicsFormat.None;
        blurDescriptor.width /= 2;
        blurDescriptor.height /= 2;
        bilateralBlurTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, blurDescriptor, "SAO_Bilateral_Blur_Texture", false, FilterMode.Bilinear);
        blurDescriptor.graphicsFormat = GraphicsFormat.R8_UNorm;
        ssaoTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, blurDescriptor, k_SSAOTextureName, false, FilterMode.Bilinear);

        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_AOBufferSampler.name, out var passData, s_AOBufferSampler))
        {
            builder.UseTexture(depthTexture, IBaseRenderGraphBuilder.AccessFlags.Read);

            passData.saoMaterial = m_ScalableAOMaterial;
            passData.saoBufferTexture = builder.UseTextureFragment(saoBufferTexture, 0, IBaseRenderGraphBuilder.AccessFlags.WriteAll);

            // Setup material params
            Matrix4x4 projectionMatrix = GL.GetGPUProjectionMatrix(renderingData.camera.projectionMatrix, true);
            var invProjection = projectionMatrix.inverse;
            passData.saoMaterial.SetVector(SAOMaterialParams._PositionParams, new Vector4(invProjection.m00 * 2.0f, invProjection.m11 * 2.0f, 0.0f, 0.0f));

            float projectionScale = Mathf.Min(0.5f * projectionMatrix.m00 * saoDescriptor.width, 0.5f * projectionMatrix.m11 * saoDescriptor.height);
            const float radius = 0.3f;
            const float spiralTurns = 14.0f;
            const float sampleCount = 32.0f;
            float inc = (1.0f / (sampleCount - 0.5f)) * spiralTurns * (2.0f * Mathf.PI);
            Vector2 angleIncCosSin = new Vector2(Mathf.Cos(inc), Mathf.Sin(inc));
            passData.saoMaterial.SetVector(SAOMaterialParams._SAO_Params, new Vector4(projectionScale * radius, sampleCount, angleIncCosSin.x, angleIncCosSin.y));

            builder.AllowPassCulling(false);

            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
            {
                Blitter.BlitTexture(context.cmd, data.saoBufferTexture, new Vector4(1f, 1f, 0f, 0f), data.saoMaterial, 0);
            });
        }

        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_BilateralBlurSampler.name, out var passData, s_BilateralBlurSampler))
        {
            builder.UseTexture(saoBufferTexture, IBaseRenderGraphBuilder.AccessFlags.Read);

            passData.saoMaterial = m_ScalableAOMaterial;
            builder.UseTextureFragment(bilateralBlurTexture, 0, IBaseRenderGraphBuilder.AccessFlags.WriteAll);

            builder.AllowPassCulling(false);

            const float bilateralThreshold = 0.0625f;
            Vector2 offsetInTexel = new Vector2(1.0f, 1.0f);
            Vector2 axisOffset = new Vector2(offsetInTexel.x / blurDescriptor.width, offsetInTexel.y / blurDescriptor.height);
            float far = renderingData.camera.farClipPlane;
            float farPlaneOverEdgeDistance = -far / bilateralThreshold;
            passData.saoMaterial.SetVector(SAOMaterialParams._BilateralBlurParams, new Vector4(axisOffset.x, axisOffset.y, farPlaneOverEdgeDistance, 0.0f));

            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
            {
                Blitter.BlitTexture(context.cmd, saoBufferTexture, new Vector4(1f, 1f, 0f, 0f), data.saoMaterial, 1);
            });
        }

        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_FinalBilateralBlurSampler.name, out var passData, s_FinalBilateralBlurSampler))
        {
            builder.UseTexture(bilateralBlurTexture, IBaseRenderGraphBuilder.AccessFlags.Read);

            passData.saoMaterial = m_ScalableAOMaterial;

            builder.UseTextureFragment(ssaoTexture, 0, IBaseRenderGraphBuilder.AccessFlags.WriteAll);

            builder.AllowPassCulling(false);

            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
            {
                Blitter.BlitTexture(context.cmd, bilateralBlurTexture, new Vector4(1f, 1f, 0f, 0f), data.saoMaterial, 2);
            });
        }

        RenderingUtils.SetGlobalRenderGraphTextureName(renderGraph, k_SSAOTextureName, ssaoTexture, "Set Global SSAO Texture");
    }

    public void Dispose()
    {
        CoreUtils.Destroy(m_ScalableAOMaterial);
    }
}
