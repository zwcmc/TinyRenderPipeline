using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class MainLightShadowPass
{
    private int m_MainLightShadowmapID;
    private RTHandle m_MainLightShadowmapTexture;

    private const int k_ShadowmapBufferBits = 16;
    private const string k_ShadowmapTextureName = "_MainLightShadowmapTexture";

    private static readonly ProfilingSampler m_ProfilingSampler = new ProfilingSampler("MainLightShadow");

    private bool m_EmptyShadowmap;


    private static class MainLightShadowConstantBuffer
    {
        public static int _WorldToShadow;
        public static int _ShadowParams;
    }

    public MainLightShadowPass()
    {
        m_MainLightShadowmapID = Shader.PropertyToID(k_ShadowmapTextureName);
        MainLightShadowConstantBuffer._WorldToShadow = Shader.PropertyToID("_MainLightWorldToShadow");
        MainLightShadowConstantBuffer._ShadowParams = Shader.PropertyToID("_MainLightShadowParams");
    }

    public bool Setup(ref RenderingData renderingData)
    {
        Clear();

        int mainLightIndex = renderingData.mainLightIndex;
        if (mainLightIndex == -1)
        {
            return SetupForEmptyShadowmap();
        }

        VisibleLight shadowLight = renderingData.cullResults.visibleLights[mainLightIndex];
        // Main light is always a directional light
        if (shadowLight.lightType != LightType.Directional)
        {
            return SetupForEmptyShadowmap();
        }

        // Check light's shadow settings
        Light light = shadowLight.light;
        if (light.shadows == LightShadows.None || light.shadowStrength <= 0f)
        {
            return SetupForEmptyShadowmap();
        }

        // Check if the light affects as least one shadow casting object in scene
        if (!renderingData.cullResults.GetShadowCasterBounds(mainLightIndex, out Bounds bounds))
        {
            return SetupForEmptyShadowmap();
        }

        int shadowmapWidth = renderingData.mainLightShadowmapWidth;
        int shadowmapHeight = renderingData.mainLightShadowmapHeight;
        ShadowRTReAllocateIfNeeded(ref m_MainLightShadowmapTexture, shadowmapWidth, shadowmapHeight, k_ShadowmapBufferBits, name: k_ShadowmapTextureName);
        m_EmptyShadowmap = false;

        return true;
    }

    public void Render(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cmd = renderingData.commandBuffer;
        if (m_EmptyShadowmap)
        {
            cmd.SetGlobalTexture(m_MainLightShadowmapID, m_MainLightShadowmapTexture.nameID);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            return;
        }

        // Setup render target and clear target
        // CoreUtils.SetRenderTarget(cmd, m_MainLightShadowmapTexture, ClearFlag.All, Color.black);
        cmd.SetRenderTarget(m_MainLightShadowmapTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
        cmd.ClearRenderTarget(true, true, Color.clear);
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        using (new ProfilingScope(cmd, m_ProfilingSampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            ref var cullResults = ref renderingData.cullResults;
            int shadowLightIndex = renderingData.mainLightIndex;

            VisibleLight shadowLight = cullResults.visibleLights[shadowLightIndex];

            int shadowResolution = Math.Min(renderingData.mainLightShadowmapWidth, renderingData.mainLightShadowmapHeight);

            var shadowDrawingSettings = new ShadowDrawingSettings(cullResults, shadowLightIndex);

            ExtractDirectionalLightMatrix(ref cullResults, shadowLightIndex, 0, 1, new Vector3(1.0f, 0.0f, 0.0f),
                shadowResolution, 0.0f, out Matrix4x4 viewMatrix, out Matrix4x4 projectionMatrix, out ShadowSplitData splitData);

            shadowDrawingSettings.splitData = splitData;

            cmd.SetViewProjectionMatrices(viewMatrix, projectionMatrix);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            context.DrawShadows(ref shadowDrawingSettings);


            cmd.SetGlobalTexture(m_MainLightShadowmapID, m_MainLightShadowmapTexture.nameID);
            cmd.SetGlobalMatrix(MainLightShadowConstantBuffer._WorldToShadow, GetShadowTransform(projectionMatrix, viewMatrix));
            cmd.SetGlobalVector(MainLightShadowConstantBuffer._ShadowParams, new Vector4(shadowLight.light.shadowStrength, 0.0f, 0.0f, 0.0f));

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
    }

    public void Dispose()
    {
        m_MainLightShadowmapTexture?.Release();
    }

    private bool SetupForEmptyShadowmap()
    {
        m_EmptyShadowmap = true;
        ShadowRTReAllocateIfNeeded(ref m_MainLightShadowmapTexture, 1, 1, k_ShadowmapBufferBits, k_ShadowmapTextureName);
        return true;
    }

    private void Clear()
    {

    }

    private static Matrix4x4 GetShadowTransform(Matrix4x4 proj, Matrix4x4 view)
    {
        // Currently CullResults ComputeDirectionalShadowMatricesAndCullingPrimitives doesn't
        // apply z reversal to projection matrix. We need to do it manually here.
        if (SystemInfo.usesReversedZBuffer)
        {
            proj.m20 = -proj.m20;
            proj.m21 = -proj.m21;
            proj.m22 = -proj.m22;
            proj.m23 = -proj.m23;
        }

        Matrix4x4 worldToShadow = proj * view;

        // Convert from clip space coordinates [-1, 1] to texture coordinates [0, 1].
        var textureScaleAndBias = Matrix4x4.identity;
        textureScaleAndBias.m00 = 0.5f;
        textureScaleAndBias.m11 = 0.5f;
        textureScaleAndBias.m22 = 0.5f;
        textureScaleAndBias.m03 = 0.5f;
        textureScaleAndBias.m23 = 0.5f;
        textureScaleAndBias.m13 = 0.5f;
        // textureScaleAndBias maps texture space coordinates from [-1,1] to [0,1]

        // Apply texture scale and offset to save a MAD in shader.
        return textureScaleAndBias * worldToShadow;
    }

    private static void ExtractDirectionalLightMatrix(ref CullingResults cullResults, int shadowLightIndex, int cascadeIndex, int cascadeCount, Vector3 cascadesSplit,
        int shadowResolution, float shadowNearPlane, out Matrix4x4 viewMatrix, out Matrix4x4 projectionMatrix, out ShadowSplitData splitData)
    {
        cullResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(shadowLightIndex, cascadeIndex, cascadeCount,
            cascadesSplit, shadowResolution, shadowNearPlane, out viewMatrix, out projectionMatrix, out splitData);
    }

    private static RenderTextureDescriptor GetTemporaryShadowTextureDescriptor(int width, int height, int bits)
    {
        var format = GraphicsFormatUtility.GetDepthStencilFormat(bits, 0);
        RenderTextureDescriptor rtd = new RenderTextureDescriptor(width, height, GraphicsFormat.None, format);
        rtd.shadowSamplingMode = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.Shadowmap) ? ShadowSamplingMode.CompareDepths : ShadowSamplingMode.None;
        return rtd;
    }

    private static bool ShadowRTNeedsReAlloc(RTHandle handle, int width, int height, int bits, string name)
    {
        if (handle == null || handle.rt == null)
            return true;

        var descriptor = GetTemporaryShadowTextureDescriptor(width, height, bits);
        TextureDesc shadowDesc = RenderingUtils.CreateTextureDesc(descriptor, TextureSizeMode.Explicit, FilterMode.Bilinear, TextureWrapMode.Clamp, name);
        return RenderingUtils.RTHandleNeedsReAlloc(handle, shadowDesc, false);
    }

    private static RTHandle AllocShadowRT(int width, int height, int bits, string name)
    {
        var rtd = GetTemporaryShadowTextureDescriptor(width, height, bits);
        return RTHandles.Alloc(rtd, FilterMode.Bilinear, TextureWrapMode.Clamp, isShadowMap: true, name: name);
    }

    private static bool ShadowRTReAllocateIfNeeded(ref RTHandle handle, int width, int height, int bits, string name = "")
    {
        if (ShadowRTNeedsReAlloc(handle, width, height, bits, name))
        {
            handle?.Release();
            handle = AllocShadowRT(width, height, bits, name);
            return true;
        }

        return false;
    }
}
