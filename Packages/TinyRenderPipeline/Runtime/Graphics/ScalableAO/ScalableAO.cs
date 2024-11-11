using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class ScalableAO
{
    private static readonly ProfilingSampler s_ScalableAmbientObscuranceSampler = new ProfilingSampler("Scalable Ambient Obscurance");
    private static readonly ProfilingSampler s_AOBufferSampler = new ProfilingSampler("AO Buffer");
    private static readonly ProfilingSampler s_BilateralBlurSampler = new ProfilingSampler("Bilateral Blur");

    private const string k_SSAOTextureName = "_ScreenSpaceOcclusionTexture";

    Material m_ScalableAOMaterial;

    private static class SAOMaterialParamShaderIDs
    {
        public static readonly int PositionParams = Shader.PropertyToID("_PositionParams");
        public static readonly int SaoParams = Shader.PropertyToID("_SaoParams");
        public static readonly int BilateralBlurParams = Shader.PropertyToID("_BilateralBlurParams");
        public static readonly int StepTapRadius = Shader.PropertyToID("_StepTapRadius");
    }

    private class PassData
    {
        public Material saoMaterial;
        public TextureHandle depthTexture;
        public TextureHandle saoBufferTexture;
        public TextureHandle bilateralBlurTexture;
        public TextureHandle ssaoTexture;
    }

    private DepthPyramidGenerator m_DepthPyramidGenerator;

    public ScalableAO(ComputeShader shader)
    {
        m_DepthPyramidGenerator = new DepthPyramidGenerator(shader);

        m_ScalableAOMaterial = CoreUtils.CreateEngineMaterial("Hidden/Tiny Render Pipeline/ScalableAO");
    }

    public void RecordRenderGraph(RenderGraph renderGraph, in TextureHandle depthTexture, out TextureHandle ssaoTexture, ref RenderingData renderingData)
    {
        // Generate depth pyramid
        TextureHandle depthPyramidTexture;
        m_DepthPyramidGenerator.RenderLinearDepthPyramid(renderGraph, in depthTexture, out depthPyramidTexture, ref renderingData);

        TextureHandle saoBufferTexture;
        TextureHandle bilateralBlurTexture;

        // Create texture handles
        var saoDescriptor = renderingData.cameraData.targetDescriptor;
        saoDescriptor.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
        saoDescriptor.depthStencilFormat = GraphicsFormat.None;
        saoBufferTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, saoDescriptor, "_SAO_Buffer_Texture", false, FilterMode.Bilinear);

        var blurDescriptor = renderingData.cameraData.targetDescriptor;
        blurDescriptor.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
        blurDescriptor.depthStencilFormat = GraphicsFormat.None;
        bilateralBlurTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, blurDescriptor, "SAO_Bilateral_Blur_Texture", false, FilterMode.Bilinear);
        blurDescriptor.graphicsFormat = GraphicsFormat.R8_UNorm;
        ssaoTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, blurDescriptor, k_SSAOTextureName, false, FilterMode.Bilinear);

        using (var builder = renderGraph.AddLowLevelPass<PassData>(s_ScalableAmbientObscuranceSampler.name, out var passData, s_ScalableAmbientObscuranceSampler))
        {
            passData.saoMaterial = m_ScalableAOMaterial;
            passData.depthTexture = depthPyramidTexture;
            passData.saoBufferTexture = saoBufferTexture;
            passData.bilateralBlurTexture = bilateralBlurTexture;
            passData.ssaoTexture = ssaoTexture;

            // Setup material params
            const float radius = 0.3f;
            const float sampleCount = 9.0f;

            ref var cameraData = ref renderingData.cameraData;
            Matrix4x4 projectionMatrix = GL.GetGPUProjectionMatrix(FrameHistory.GetCurrentFrameJitteredProjection(), true);
            var invProjection = Matrix4x4.Inverse(projectionMatrix);

            // 屏幕的宽高(以像素为单位)
            Vector2Int screenSizeInPixels = new Vector2Int(saoDescriptor.width, saoDescriptor.height);
            // 投影矩阵将 xy 转换到 [-1,1] , 也就是 2 的范围 , 所以这里乘以 0.5
            float projectionScale = Mathf.Min(0.5f * projectionMatrix.m00 * screenSizeInPixels.x, 0.5f * projectionMatrix.m11 * screenSizeInPixels.y);
            // 计算经过投影矩阵缩放后的屏幕空间内范围大小(以像素为单位)
            float projectionScaledRadius = projectionScale * radius;

            passData.saoMaterial.SetVector(SAOMaterialParamShaderIDs.PositionParams, new Vector4(invProjection.m00 * 2.0f, invProjection.m11 * 2.0f, projectionScaledRadius, 8.0f));

            const float spiralTurns = 6.0f;
            const float stepTapRadius = 1.0f / (sampleCount - 0.5f);
            // 每次采样旋转的角度
            float stepTapAngle = stepTapRadius * spiralTurns * (2.0f * Mathf.PI);
            // 计算这个旋转角度的 cos 和 sin 值, 并在后续的 Shader 计算中构建一个 2x2 的旋转矩阵
            Vector2 angleIncCosSin = new Vector2(Mathf.Cos(stepTapAngle), Mathf.Sin(stepTapAngle));

            passData.saoMaterial.SetFloat(SAOMaterialParamShaderIDs.StepTapRadius, stepTapRadius);
            passData.saoMaterial.SetVector(SAOMaterialParamShaderIDs.SaoParams, new Vector4(radius, sampleCount, angleIncCosSin.x, angleIncCosSin.y));

            const float blurSampleCount = 6.0f;
            const float bilateralThreshold = 0.116f;
            Vector2 offsetInTexel = new Vector2(1.0f, 1.0f);
            Vector2 axisOffset = new Vector2(offsetInTexel.x / blurDescriptor.width, offsetInTexel.y / blurDescriptor.height);
            float far = cameraData.camera.farClipPlane;
            float farPlaneOverEdgeDistance = -far / bilateralThreshold;
            passData.saoMaterial.SetVector(SAOMaterialParamShaderIDs.BilateralBlurParams, new Vector4(axisOffset.x, axisOffset.y, farPlaneOverEdgeDistance, blurSampleCount));

            builder.UseTexture(depthPyramidTexture, IBaseRenderGraphBuilder.AccessFlags.Read);

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
