using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public class TinyBaseRenderer : IDisposable
{
    private Material m_BlitMaterial;
    private Material m_CopyDepthMaterial;

    protected ForwardLights forwardLights;

    protected MainLightShadowPass mainLightShadowPass;
    protected AdditionalLightsShadowPass additionalLightsShadowPass;

    protected ColorGradingLutPass colorGradingLutPass;

    protected DrawObjectsForwardPass renderOpaqueForwardPass;
    protected CopyDepthPass copyDepthPass;
    protected DrawSkyboxPass renderSkyboxPass;
    protected CopyColorPass copyColorPass;
    protected DrawObjectsForwardPass renderTransparentForwardPass;

    protected PostProcessingPass postProcessingPass;
    protected FXAAPass fxaaPass;
    protected FinalBlitPass finalBlitPass;

#if UNITY_EDITOR
    protected CopyDepthPass finalDepthCopyPass;
#endif

    protected TinyBaseRenderer(TinyRenderPipelineAsset asset)
    {
        if (asset.shaders != null)
        {
            m_BlitMaterial = CoreUtils.CreateEngineMaterial(asset.shaders.blitShader);
            m_CopyDepthMaterial = CoreUtils.CreateEngineMaterial(asset.shaders.copyDepthShader);
        }

        forwardLights = new ForwardLights();

        mainLightShadowPass = new MainLightShadowPass();
        additionalLightsShadowPass = new AdditionalLightsShadowPass();

        colorGradingLutPass = new ColorGradingLutPass();

        renderOpaqueForwardPass = new DrawObjectsForwardPass(true);
        copyDepthPass = new CopyDepthPass(m_CopyDepthMaterial);
        renderSkyboxPass = new DrawSkyboxPass();
        copyColorPass = new CopyColorPass(m_BlitMaterial);
        renderTransparentForwardPass = new DrawObjectsForwardPass();

        postProcessingPass = new PostProcessingPass();
        fxaaPass = new FXAAPass();
        finalBlitPass = new FinalBlitPass(m_BlitMaterial);

#if UNITY_EDITOR
        finalDepthCopyPass = new CopyDepthPass(m_CopyDepthMaterial, true);
#endif
    }

    public void Dispose()
    {
        mainLightShadowPass?.Dispose();
        additionalLightsShadowPass?.Dispose();

        colorGradingLutPass?.Dispose();

        postProcessingPass?.Dispose();
        fxaaPass?.Dispose();

        CoreUtils.Destroy(m_BlitMaterial);
        CoreUtils.Destroy(m_CopyDepthMaterial);

        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) { }

    protected static void SetPerCameraShaderVariables(RasterCommandBuffer cmd, Camera camera, bool isTargetFlipped)
    {
        float near = camera.nearClipPlane;
        float far = camera.farClipPlane;
        float invNear = Mathf.Approximately(near, 0.0f) ? 0.0f : 1.0f / near;
        float invFar = Mathf.Approximately(far, 0.0f) ? 0.0f : 1.0f / far;
        float isOrthographic = camera.orthographic ? 1.0f : 0.0f;

        // From http://www.humus.name/temp/Linearize%20depth.txt
        // But as depth component textures on OpenGL always return in 0..1 range (as in D3D), we have to use
        // the same constants for both D3D and OpenGL here.
        // OpenGL would be this:
        // zc0 = (1.0 - far / near) / 2.0;
        // zc1 = (1.0 + far / near) / 2.0;
        // D3D is this:
        float zc0 = 1.0f - far * invNear;
        float zc1 = far * invNear;

        Vector4 zBufferParams = new Vector4(zc0, zc1, zc0 * invFar, zc1 * invFar);
        if (SystemInfo.usesReversedZBuffer)
        {
            zBufferParams.y += zBufferParams.x;
            zBufferParams.x = -zBufferParams.x;
            zBufferParams.w += zBufferParams.z;
            zBufferParams.z = -zBufferParams.z;
        }

        cmd.SetGlobalVector(ShaderPropertyId.worldSpaceCameraPos, camera.transform.position);
        cmd.SetGlobalVector(ShaderPropertyId.zBufferParams, zBufferParams);
        float aspectRatio = (float)camera.pixelWidth / (float)camera.pixelHeight;
        float orthographicSize = camera.orthographicSize;
        Vector4 orthoParams = new Vector4(orthographicSize * aspectRatio, orthographicSize, 0.0f, isOrthographic);
        cmd.SetGlobalVector(ShaderPropertyId.orthoParams, orthoParams);
        float projectionFlipSign = isTargetFlipped ? -1.0f : 1.0f;
        cmd.SetGlobalVector(ShaderPropertyId.projectionParams, new Vector4(projectionFlipSign, near, far, 1.0f * invFar));
    }
}
