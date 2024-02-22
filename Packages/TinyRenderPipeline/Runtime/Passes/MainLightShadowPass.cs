using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class MainLightShadowPass
{
    private int m_MainLightShadowmapID;
    private RTHandle m_MainLightShadowmapTexture;

    private const int k_MaxCascades = 4;
    private const int k_ShadowmapBufferBits = 16;
    private const string k_ShadowmapTextureName = "_MainLightShadowmapTexture";

    private Matrix4x4[] m_MainLightShadowMatrices;
    private Vector4[] m_CascadesSplitDistance;

    private bool m_EmptyShadowmap;

    private float m_CascadeBorder;
    private float m_MaxShadowDistanceSq;

    private static readonly ProfilingSampler m_ProfilingSampler = new ProfilingSampler("MainLightShadow");

    private static class MainLightShadowConstantBuffer
    {
        public static int _WorldToShadow;
        public static int _ShadowParams;
        public static int _CascadeShadowSplitSpheres0;
        public static int _CascadeShadowSplitSpheres1;
        public static int _CascadeShadowSplitSpheres2;
        public static int _CascadeShadowSplitSpheres3;
        public static int _CascadeShadowSplitSphereRadii;
    }
    
    private struct ShadowCascadeData
    {
        public Matrix4x4 viewMatrix;
        public Matrix4x4 projectionMatrix;
        public Matrix4x4 shadowTransform;
        public int offsetX;
        public int offsetY;
        public int resolution;
        public ShadowSplitData splitData;
    }

    public MainLightShadowPass()
    {
        m_MainLightShadowMatrices = new Matrix4x4[k_MaxCascades + 1];
        m_CascadesSplitDistance = new Vector4[k_MaxCascades];

        m_MainLightShadowmapID = Shader.PropertyToID(k_ShadowmapTextureName);
        MainLightShadowConstantBuffer._WorldToShadow = Shader.PropertyToID("_MainLightWorldToShadow");
        MainLightShadowConstantBuffer._ShadowParams = Shader.PropertyToID("_MainLightShadowParams");
        MainLightShadowConstantBuffer._CascadeShadowSplitSpheres0 = Shader.PropertyToID("_CascadeShadowSplitSpheres0");
        MainLightShadowConstantBuffer._CascadeShadowSplitSpheres1 = Shader.PropertyToID("_CascadeShadowSplitSpheres1");
        MainLightShadowConstantBuffer._CascadeShadowSplitSpheres2 = Shader.PropertyToID("_CascadeShadowSplitSpheres2");
        MainLightShadowConstantBuffer._CascadeShadowSplitSpheres3 = Shader.PropertyToID("_CascadeShadowSplitSpheres3");
        MainLightShadowConstantBuffer._CascadeShadowSplitSphereRadii = Shader.PropertyToID("_CascadeShadowSplitSphereRadii");
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

        ref var shadowData = ref renderingData.shadowData;

        int shadowmapWidth = shadowData.mainLightShadowmapWidth;
        int shadowmapHeight = shadowData.mainLightShadowmapHeight;
        ShadowRTReAllocateIfNeeded(ref m_MainLightShadowmapTexture, shadowmapWidth, shadowmapHeight, k_ShadowmapBufferBits, name: k_ShadowmapTextureName);

        m_MaxShadowDistanceSq = shadowData.maxShadowDistance * shadowData.maxShadowDistance;
        m_CascadeBorder = shadowData.mainLightShadowCascadeBorder;
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

            var shadowDrawingSettings = new ShadowDrawingSettings(cullResults, shadowLightIndex);

            int cascadesCount = renderingData.shadowData.cascadesCount;
            Vector3 cascadesSplit = renderingData.shadowData.cascadesSplit;

            int cascadeResolution = GetMaxTileResolutionInAtlas(renderingData.shadowData.mainLightShadowmapWidth, renderingData.shadowData.mainLightShadowmapHeight, cascadesCount);

            int renderTargetWidth = renderingData.shadowData.mainLightShadowmapWidth;
            int renderTargetHeight = (cascadesCount == 2) ? renderingData.shadowData.mainLightShadowmapHeight >> 1 : renderingData.shadowData.mainLightShadowmapHeight;

            for (int i = 0; i < cascadesCount; ++i)
            {
                ExtractDirectionalLightMatrix(ref cullResults, shadowLightIndex, i, cascadesCount, cascadesSplit,
                    renderTargetWidth, renderTargetHeight, cascadeResolution, 0.0f, out ShadowCascadeData shadowCascadeData);

                shadowDrawingSettings.splitData = shadowCascadeData.splitData;

                cmd.SetGlobalDepthBias(1.0f, 2.5f);

                cmd.SetViewport(new Rect(shadowCascadeData.offsetX, shadowCascadeData.offsetY, shadowCascadeData.resolution, shadowCascadeData.resolution));
                cmd.SetViewProjectionMatrices(shadowCascadeData.viewMatrix, shadowCascadeData.projectionMatrix);

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                m_MainLightShadowMatrices[i] = shadowCascadeData.shadowTransform;
                m_CascadesSplitDistance[i] = shadowCascadeData.splitData.cullingSphere;

                context.DrawShadows(ref shadowDrawingSettings);

                cmd.SetGlobalDepthBias(0.0f, 0.0f);
            }

            // We setup and additional a no-op WorldToShadow matrix in the last index
            // because the ComputeCascadeIndex function in Shadows.hlsl can return an index
            // out of bounds. (position not inside any cascade) and we want to avoid branching
            Matrix4x4 noOpShadowMatrix = Matrix4x4.zero;
            noOpShadowMatrix.m22 = (SystemInfo.usesReversedZBuffer) ? 1.0f : 0.0f;
            for (int i = cascadesCount; i <= k_MaxCascades; ++i)
                m_MainLightShadowMatrices[i] = noOpShadowMatrix;

            cmd.SetGlobalTexture(m_MainLightShadowmapID, m_MainLightShadowmapTexture.nameID);

            GetScaleAndBiasForLinearDistanceFade(m_MaxShadowDistanceSq, m_CascadeBorder, out float shadowFadeScale, out float shadowFadeBias);

            cmd.SetGlobalMatrixArray(MainLightShadowConstantBuffer._WorldToShadow, m_MainLightShadowMatrices);
            cmd.SetGlobalVector(MainLightShadowConstantBuffer._ShadowParams, new Vector4(shadowLight.light.shadowStrength, 0.0f, shadowFadeScale, shadowFadeBias));

            if (cascadesCount > 1)
            {
                cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadeShadowSplitSpheres0, m_CascadesSplitDistance[0]);
                cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadeShadowSplitSpheres1, m_CascadesSplitDistance[1]);
                cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadeShadowSplitSpheres2, m_CascadesSplitDistance[2]);
                cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadeShadowSplitSpheres3, m_CascadesSplitDistance[3]);
                cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadeShadowSplitSphereRadii, new Vector4(
                    m_CascadesSplitDistance[0].w * m_CascadesSplitDistance[0].w,
                    m_CascadesSplitDistance[1].w * m_CascadesSplitDistance[1].w,
                    m_CascadesSplitDistance[2].w * m_CascadesSplitDistance[2].w,
                    m_CascadesSplitDistance[3].w * m_CascadesSplitDistance[3].w)
                );
            }

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
        for (int i = 0; i < m_MainLightShadowMatrices.Length; ++i)
            m_MainLightShadowMatrices[i] = Matrix4x4.identity;

        for (int i = 0; i < m_CascadesSplitDistance.Length; ++i)
            m_CascadesSplitDistance[i] = Vector4.zero;
    }

    private static void GetScaleAndBiasForLinearDistanceFade(float maxShadowDistanceSq, float border, out float scale, out float bias)
    {
        // To avoid division from zero
        // This values ensure that fade within cascade will be 0 and outside 1
        if (border < 0.0001f)
        {
            float multiplier = 1000f; // To avoid blending if difference is in fractions
            scale = multiplier;
            bias = -maxShadowDistanceSq * multiplier;
            return;
        }

        // Distance near fade
        border = 1.0f - border;
        border *= border;
        float distanceFadeNearSq = border * maxShadowDistanceSq;

        // Linear distance fade:
        // (x - nearFade) / (maxDistance - nearFade) then
        //  x * (1.0 / (maxDistance - nearFade)) + (-nearFade / (maxDistance - nearFade)) then
        // scale = 1.0 / (maxDistance - nearFade)
        // bias = -nearFade / (maxDistance - nearFade)
        scale = 1.0f / (maxShadowDistanceSq - distanceFadeNearSq);
        bias = -distanceFadeNearSq / (maxShadowDistanceSq - distanceFadeNearSq);
    }

    private static int GetMaxTileResolutionInAtlas(int atlasWidth, int atlasHeight, int tileCount)
    {
        int resolution = Mathf.Min(atlasWidth, atlasHeight);
        int currentTileCount = atlasWidth / resolution * atlasHeight / resolution;
        while (currentTileCount < tileCount)
        {
            resolution = resolution >> 1;
            currentTileCount = atlasWidth / resolution * atlasHeight / resolution;
        }

        return resolution;
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

    private static void ApplySliceTransform(ref ShadowCascadeData shadowCascadeData, int atlasWidth, int atlasHeight)
    {
        Matrix4x4 sliceTransform = Matrix4x4.identity;
        float oneOverAtlasWidth = 1.0f / atlasWidth;
        float oneOverAtlasHeight = 1.0f / atlasHeight;
        sliceTransform.m00 = shadowCascadeData.resolution * oneOverAtlasWidth;
        sliceTransform.m11 = shadowCascadeData.resolution * oneOverAtlasHeight;
        sliceTransform.m03 = shadowCascadeData.offsetX * oneOverAtlasWidth;
        sliceTransform.m13 = shadowCascadeData.offsetY * oneOverAtlasHeight;

        // Apply shadow slice scale and offset
        shadowCascadeData.shadowTransform = sliceTransform * shadowCascadeData.shadowTransform;
    }

    private static void ExtractDirectionalLightMatrix(ref CullingResults cullResults, int shadowLightIndex, int cascadeIndex, int cascadeCount, Vector3 cascadesSplit,
        int shadowmapWidth, int shadowmapHeight, int shadowResolution, float shadowNearPlane, out ShadowCascadeData shadowCascadeData)
    {
        cullResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(shadowLightIndex, cascadeIndex, cascadeCount,
            cascadesSplit, shadowResolution, shadowNearPlane, out shadowCascadeData.viewMatrix, out shadowCascadeData.projectionMatrix, out shadowCascadeData.splitData);

        shadowCascadeData.resolution = shadowResolution;
        shadowCascadeData.offsetX = (cascadeIndex % 2) * shadowResolution;
        shadowCascadeData.offsetY = (cascadeIndex / 2) * shadowResolution;
        shadowCascadeData.shadowTransform = GetShadowTransform(shadowCascadeData.projectionMatrix, shadowCascadeData.viewMatrix);

        if (cascadeCount > 1)
            ApplySliceTransform(ref shadowCascadeData, shadowmapWidth, shadowmapHeight);
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
