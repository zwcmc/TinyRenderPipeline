using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class ScalableAOPass
{
    private static readonly ProfilingSampler s_ScalableAmbientObscuranceSampler = new ProfilingSampler("Scalable Ambient Obscurance");
    private static readonly ProfilingSampler s_AOBufferSampler = new ProfilingSampler("AO Buffer");
    private static readonly ProfilingSampler s_BilateralBlurSampler = new ProfilingSampler("Bilateral Blur");

    private const string k_SSAOTextureName = "_ScreenSpaceOcclusionTexture";

    Material m_ScalableAOMaterial;

    private static class SAOMaterialParamShaderIDs
    {
        public static readonly int PositionParams = Shader.PropertyToID("_PositionParams");
        public static readonly int SaoParams = Shader.PropertyToID("_SAO_Params");
        public static readonly int BilateralBlurParams = Shader.PropertyToID("_BilateralBlurParams");
    }

    private class PassData
    {
        public Material saoMaterial;
        public TextureHandle depthTexture;
        public TextureHandle saoBufferTexture;
        public TextureHandle bilateralBlurTexture;
        public TextureHandle ssaoTexture;
    }

    public ScalableAOPass()
    {
        m_ScalableAOMaterial = CoreUtils.CreateEngineMaterial("Hidden/Tiny Render Pipeline/ScalableAO");
    }

    public void RecordRenderGraph(RenderGraph renderGraph, in TextureHandle depthTexture, out TextureHandle ssaoTexture, ref RenderingData renderingData)
    {
        TextureHandle saoBufferTexture;
        TextureHandle bilateralBlurTexture;

        // Create texture handles
        var saoDescriptor = renderingData.cameraTargetDescriptor;
        saoDescriptor.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
        saoDescriptor.depthStencilFormat = GraphicsFormat.None;
        saoBufferTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, saoDescriptor, "_SAO_Buffer_Texture", false, FilterMode.Bilinear);

        var blurDescriptor = renderingData.cameraTargetDescriptor;
        blurDescriptor.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
        blurDescriptor.depthStencilFormat = GraphicsFormat.None;
        bilateralBlurTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, blurDescriptor, "SAO_Bilateral_Blur_Texture", false, FilterMode.Bilinear);
        blurDescriptor.graphicsFormat = GraphicsFormat.R8_UNorm;
        ssaoTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, blurDescriptor, k_SSAOTextureName, false, FilterMode.Bilinear);

        using (var builder = renderGraph.AddLowLevelPass<PassData>(s_ScalableAmbientObscuranceSampler.name, out var passData, s_ScalableAmbientObscuranceSampler))
        {
            passData.saoMaterial = m_ScalableAOMaterial;
            passData.depthTexture = depthTexture;
            passData.saoBufferTexture = saoBufferTexture;
            passData.bilateralBlurTexture = bilateralBlurTexture;
            passData.ssaoTexture = ssaoTexture;

            // Setup material params
            Matrix4x4 projectionMatrix = GL.GetGPUProjectionMatrix(renderingData.camera.projectionMatrix, true);
            var invProjection = projectionMatrix.inverse;
            passData.saoMaterial.SetVector(SAOMaterialParamShaderIDs.PositionParams, new Vector4(invProjection.m00 * 2.0f, invProjection.m11 * 2.0f, 0.0f, 0.0f));

            float projectionScale = Mathf.Min(0.5f * projectionMatrix.m00 * saoDescriptor.width, 0.5f * projectionMatrix.m11 * saoDescriptor.height);
            const float radius = 0.5f;
            const float spiralTurns = 14.0f;
            const float sampleCount = 32.0f;
            float inc = (1.0f / (sampleCount - 0.5f)) * spiralTurns * (2.0f * Mathf.PI);
            Vector2 angleIncCosSin = new Vector2(Mathf.Cos(inc), Mathf.Sin(inc));
            passData.saoMaterial.SetVector(SAOMaterialParamShaderIDs.SaoParams, new Vector4(projectionScale * radius, sampleCount, angleIncCosSin.x, angleIncCosSin.y));

            const float blurSampleCount = 6.0f;
            const float bilateralThreshold = 0.0516f;
            Vector2 offsetInTexel = new Vector2(1.0f, 1.0f);
            Vector2 axisOffset = new Vector2(offsetInTexel.x / blurDescriptor.width, offsetInTexel.y / blurDescriptor.height);
            float far = renderingData.camera.farClipPlane;
            float farPlaneOverEdgeDistance = -far / bilateralThreshold;
            passData.saoMaterial.SetVector(SAOMaterialParamShaderIDs.BilateralBlurParams, new Vector4(axisOffset.x, axisOffset.y, farPlaneOverEdgeDistance, blurSampleCount));

            builder.UseTexture(depthTexture, IBaseRenderGraphBuilder.AccessFlags.Read);

            builder.UseTexture(saoBufferTexture, IBaseRenderGraphBuilder.AccessFlags.ReadWrite);
            builder.UseTexture(bilateralBlurTexture, IBaseRenderGraphBuilder.AccessFlags.ReadWrite);
            builder.UseTexture(ssaoTexture, IBaseRenderGraphBuilder.AccessFlags.WriteAll);

            builder.AllowPassCulling(false);

            builder.SetRenderFunc((PassData data, LowLevelGraphContext context) =>
            {
                var cmd = context.legacyCmd;

                var loadAction = RenderBufferLoadAction.DontCare;
                var storeAction = RenderBufferStoreAction.Store;

                using (new ProfilingScope(cmd, s_AOBufferSampler))
                {
                    Blitter.BlitCameraTexture(cmd, data.depthTexture, data.saoBufferTexture, loadAction, storeAction, data.saoMaterial, 0);
                }

                using (new ProfilingScope(cmd, s_BilateralBlurSampler))
                {
                    Blitter.BlitCameraTexture(cmd, data.saoBufferTexture, data.bilateralBlurTexture, loadAction, storeAction, data.saoMaterial, 1);
                    Blitter.BlitCameraTexture(cmd, data.bilateralBlurTexture, data.ssaoTexture, loadAction, storeAction, data.saoMaterial, 2);
                }
            });
        }

        RenderingUtils.SetGlobalRenderGraphTextureName(renderGraph, k_SSAOTextureName, ssaoTexture, "Set Global SSAO Texture");
    }

    public void Dispose()
    {
        CoreUtils.Destroy(m_ScalableAOMaterial);
    }
}
