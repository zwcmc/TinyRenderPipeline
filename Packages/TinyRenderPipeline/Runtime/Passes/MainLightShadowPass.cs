using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class MainLightShadowPass
{
    private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler("MainLightShadowPass");
    private static readonly ProfilingSampler s_SetMainLightShadowSampler = new ProfilingSampler("SetMainLightShadowmapGlobal");

    // This limit matches same limit in Shadows.hlsl
    private const int k_MaxCascades = 4;
    private const int k_ShadowmapBufferBits = 16;
    private const string k_ShadowmapTextureName = "_MainLightShadowmapTexture";

    private int m_MainLightShadowmapID;
    private RTHandle m_MainLightShadowmapTexture;
    private RTHandle m_EmptyLightShadowmapTexture;

    private bool m_CreateEmptyShadowmap;

    private Matrix4x4[] m_MainLightShadowMatrices;
    private Vector4[] m_CascadesSplitDistance;

    private Vector4[] m_CascadeOffsetScales;

    private float m_CascadeBorder;
    private float m_MaxShadowDistanceSq;
    private int m_ShadowCasterCascadesCount;
    private int m_RenderTargetWidth;
    private int m_RenderTargetHeight;

    private static class MainLightShadowConstantBuffer
    {
        public static readonly int _MainLightWorldToShadow = Shader.PropertyToID("_MainLightWorldToShadow");
        public static readonly int _ShadowParams = Shader.PropertyToID("_MainLightShadowParams");
        public static readonly int _CascadeShadowSplitSpheres0 = Shader.PropertyToID("_CascadeShadowSplitSpheres0");
        public static readonly int _CascadeShadowSplitSpheres1 = Shader.PropertyToID("_CascadeShadowSplitSpheres1");
        public static readonly int _CascadeShadowSplitSpheres2 = Shader.PropertyToID("_CascadeShadowSplitSpheres2");
        public static readonly int _CascadeShadowSplitSpheres3 = Shader.PropertyToID("_CascadeShadowSplitSpheres3");
        public static readonly int _CascadesParams = Shader.PropertyToID("_CascadesParams");

        public static readonly int _CascadeOffsetScales = Shader.PropertyToID("_CascadeOffsetScales");
        public static readonly int _DirLightPCSSParams0 = Shader.PropertyToID("_DirLightPCSSParams0");
        public static readonly int _DirLightPCSSParams1 = Shader.PropertyToID("_DirLightPCSSParams1");
        public static readonly int _DirLightPCSSProjs = Shader.PropertyToID("_DirLightPCSSProjs");
    }

    private class PassData
    {
        public MainLightShadowPass pass;

        public TextureHandle shadowmapTexture;
        public RenderingData renderingData;

        public int shadowmapID;
        public bool emptyShadowmap;

        public RendererListHandle[] shadowRendererListHandle = new RendererListHandle[k_MaxCascades];
    }

    // private struct PcssCascadeData
    // {
    //     public Vector4 dirLightPcssParams0;
    //     public Vector4 dirLightPcssParams1;
    // }
    //
    // private PcssCascadeData[] m_PcssCascadeDatas;

    private Vector4[] m_DirLightPCSSParams0;
    private Vector4[] m_DirLightPCSSParams1;

    private static class PCSSLightParams
    {
        public static float dirLightAngularDiameter = 1.23f;
        public static float dirLightPcssBlockerSearchAngularDiameter = 12;
        public static float dirLightPcssMinFilterMaxAngularDiameter = 10;
        public static float dirLightPcssMaxPenumbraSize = 0.56f;
        public static float dirLightPcssMaxSamplingDistance = 0.5f;
        public static float dirLightPcssMinFilterSizeTexels = 1.5f;
        public static float dirLightPcssBlockerSamplingClumpExponent = 2f;
    }

    private Vector4[] m_DeviceProjectionVectors;

    public MainLightShadowPass()
    {
        m_MainLightShadowMatrices = new Matrix4x4[k_MaxCascades + 1];
        m_CascadesSplitDistance = new Vector4[k_MaxCascades];
        m_CascadeOffsetScales = new Vector4[k_MaxCascades + 1];
        m_DirLightPCSSParams0 = new Vector4[k_MaxCascades + 1];
        m_DirLightPCSSParams1 = new Vector4[k_MaxCascades + 1];
        m_DeviceProjectionVectors = new Vector4[k_MaxCascades + 1];

        m_MainLightShadowmapID = Shader.PropertyToID(k_ShadowmapTextureName);

        m_EmptyLightShadowmapTexture = ShadowUtils.AllocShadowRT(1, 1, k_ShadowmapBufferBits, "_EmptyLightShadowmapTexture");
    }

    public bool Setup(ref RenderingData renderingData)
    {
        if (!renderingData.shadowData.mainLightShadowsEnabled)
        {
            return SetupForEmptyRendering();
        }

        int mainLightIndex = renderingData.mainLightIndex;
        if (mainLightIndex == -1)
        {
            return SetupForEmptyRendering();
        }

        VisibleLight shadowLight = renderingData.cullResults.visibleLights[mainLightIndex];
        // Main light is always a directional light
        if (shadowLight.lightType != LightType.Directional)
        {
            return SetupForEmptyRendering();
        }

        // Check light's shadow settings
        Light light = shadowLight.light;
        if (light.shadows == LightShadows.None || light.shadowStrength <= 0f)
        {
            return SetupForEmptyRendering();
        }

        // Check if the light affects as least one shadow casting object in scene
        if (!renderingData.cullResults.GetShadowCasterBounds(mainLightIndex, out Bounds bounds))
        {
            return SetupForEmptyRendering();
        }

        // Clear data
        Clear();

        ref var shadowData = ref renderingData.shadowData;
        m_ShadowCasterCascadesCount = renderingData.shadowData.cascadesCount;
        m_RenderTargetWidth = renderingData.shadowData.mainLightShadowmapWidth;
        m_RenderTargetHeight = (m_ShadowCasterCascadesCount == 2) ? renderingData.shadowData.mainLightShadowmapHeight >> 1 : renderingData.shadowData.mainLightShadowmapHeight;
        ShadowUtils.ShadowRTReAllocateIfNeeded(ref m_MainLightShadowmapTexture, m_RenderTargetWidth, m_RenderTargetHeight, k_ShadowmapBufferBits, name: k_ShadowmapTextureName);

        m_MaxShadowDistanceSq = shadowData.maxShadowDistance * shadowData.maxShadowDistance;
        m_CascadeBorder = shadowData.mainLightShadowCascadeBorder;

        m_CreateEmptyShadowmap = false;

        return true;
    }

    public TextureHandle Record(RenderGraph renderGraph, ref RenderingData renderingData)
    {
        TextureHandle shadowTexture;

        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_ProfilingSampler.name, out var passData, s_ProfilingSampler))
        {
            InitPassData(renderGraph, ref renderingData, ref passData);

            if (!m_CreateEmptyShadowmap)
            {
                for (int cascadeIndex = 0; cascadeIndex < m_ShadowCasterCascadesCount; ++cascadeIndex)
                    builder.UseRendererList(passData.shadowRendererListHandle[cascadeIndex]);

                passData.shadowmapTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, m_MainLightShadowmapTexture.rt.descriptor, k_ShadowmapTextureName, true, FilterMode.Bilinear);
                builder.UseTextureFragmentDepth(passData.shadowmapTexture, IBaseRenderGraphBuilder.AccessFlags.Write);
            }

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext rasterGraphContext) =>
            {
                if (!data.emptyShadowmap)
                    data.pass.RenderMainLightCascadeShadowmap(rasterGraphContext.cmd, ref data, ref data.renderingData);
            });

            shadowTexture = passData.shadowmapTexture;
        }

        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_SetMainLightShadowSampler.name, out var passData, s_SetMainLightShadowSampler))
        {
            passData.pass = this;
            passData.shadowmapID = m_MainLightShadowmapID;
            passData.emptyShadowmap = m_CreateEmptyShadowmap;
            passData.renderingData = renderingData;

            passData.shadowmapTexture = shadowTexture;

            if (shadowTexture.IsValid())
                builder.UseTexture(shadowTexture, IBaseRenderGraphBuilder.AccessFlags.Read);

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext rasterGraphContext) =>
            {
                if (data.emptyShadowmap)
                {
                    data.pass.SetEmptyMainLightCascadeShadowmap(rasterGraphContext.cmd);
                    data.shadowmapTexture = rasterGraphContext.defaultResources.defaultShadowTexture;
                }

                rasterGraphContext.cmd.SetGlobalTexture(data.shadowmapID, data.shadowmapTexture);
            });
            return passData.shadowmapTexture;
        }
    }

    public void Dispose()
    {
        m_MainLightShadowmapTexture?.Release();
        m_EmptyLightShadowmapTexture?.Release();
    }

    private void InitPassData(RenderGraph renderGraph, ref RenderingData renderingData, ref PassData passData)
    {
        passData.pass = this;
        passData.emptyShadowmap = m_CreateEmptyShadowmap;
        passData.shadowmapID = m_MainLightShadowmapID;
        passData.renderingData = renderingData;

        int shadowLightIndex = renderingData.mainLightIndex;
        if (!m_CreateEmptyShadowmap && shadowLightIndex != -1)
        {
            var cullResults = renderingData.cullResults;
            var settings = new ShadowDrawingSettings(cullResults, shadowLightIndex);
            settings.useRenderingLayerMaskTest = true;
            for (int cascadeIndex = 0; cascadeIndex < m_ShadowCasterCascadesCount; ++cascadeIndex)
            {
                passData.shadowRendererListHandle[cascadeIndex] = renderGraph.CreateShadowRendererList(ref settings);
            }
        }
    }

    private void RenderMainLightCascadeShadowmap(RasterCommandBuffer cmd, ref PassData data, ref RenderingData renderingData)
    {
        int shadowLightIndex = renderingData.mainLightIndex;
        if (shadowLightIndex == -1)
            return;

        ref var cullResults = ref renderingData.cullResults;
        VisibleLight shadowLight = cullResults.visibleLights[shadowLightIndex];

        cmd.SetGlobalVector(ShaderPropertyId.worldSpaceCameraPos, renderingData.camera.transform.position);

        Vector3 cascadesSplit = renderingData.shadowData.cascadesSplit;
        int cascadeResolution = ShadowUtils.GetMaxTileResolutionInAtlas(renderingData.shadowData.mainLightShadowmapWidth, renderingData.shadowData.mainLightShadowmapHeight, m_ShadowCasterCascadesCount);

        // PCSS
        float invShadowmapWidth = 1.0f / m_RenderTargetWidth;
        float invShadowmapHeight = 1.0f / m_RenderTargetHeight;
        float lightAngularDiameter = PCSSLightParams.dirLightAngularDiameter;
        float dirLightDepth2Radius = Mathf.Tan(0.5f * Mathf.Deg2Rad * lightAngularDiameter);
        float minFilterAngularDiameter = Mathf.Max(PCSSLightParams.dirLightPcssBlockerSearchAngularDiameter, PCSSLightParams.dirLightPcssMinFilterMaxAngularDiameter);
        float halfMinFilterAngularDiameterTangent = Mathf.Tan(0.5f * Mathf.Deg2Rad * Mathf.Max(minFilterAngularDiameter, lightAngularDiameter));
        float halfBlockerSearchAngularDiameterTangent = Mathf.Tan(0.5f * Mathf.Deg2Rad * Mathf.Max(PCSSLightParams.dirLightPcssBlockerSearchAngularDiameter, lightAngularDiameter));

        for (int cascadeIndex = 0; cascadeIndex < m_ShadowCasterCascadesCount; ++cascadeIndex)
        {
            bool success = ShadowUtils.ExtractDirectionalLightMatrix(ref cullResults, shadowLightIndex, cascadeIndex, m_ShadowCasterCascadesCount, cascadesSplit,
                m_RenderTargetWidth, m_RenderTargetHeight, cascadeResolution, shadowLight.light.shadowNearPlane, out ShadowSliceData shadowCascadeData);

            // This cascade does not need to be rendered this frame
            if (!success)
                continue;

            Vector4 shadowBias = ShadowUtils.GetShadowBias(shadowLight, shadowLightIndex, shadowCascadeData.projectionMatrix, shadowCascadeData.resolution);
            ShadowUtils.SetupShadowCasterConstantBuffer(cmd, shadowLight, shadowBias);

            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.ShadowPCF, renderingData.shadowData.softShadows == SoftShadows.PCF);
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.ShadowPCSS, renderingData.shadowData.softShadows == SoftShadows.PCSS);

            // Set slope-scale depth bias
            cmd.SetGlobalDepthBias(1.1f, 2.2f);

            cmd.SetViewport(new Rect(shadowCascadeData.offsetX, shadowCascadeData.offsetY, shadowCascadeData.resolution, shadowCascadeData.resolution));
            cmd.SetViewProjectionMatrices(shadowCascadeData.viewMatrix, shadowCascadeData.projectionMatrix);

            RendererList shadowRendererList = data.shadowRendererListHandle[cascadeIndex];
            if (shadowRendererList.isValid)
                cmd.DrawRendererList(shadowRendererList);

            // Reset slope-scale depth bias
            cmd.SetGlobalDepthBias(0.0f, 0.0f);

            m_MainLightShadowMatrices[cascadeIndex] = shadowCascadeData.shadowTransform;
            Vector4 cullingSphere = shadowCascadeData.splitData.cullingSphere;
            cullingSphere.w *= cullingSphere.w;
            m_CascadesSplitDistance[cascadeIndex] = cullingSphere;

            if (renderingData.shadowData.softShadows == SoftShadows.PCSS)
            {
                m_CascadeOffsetScales[cascadeIndex] = new Vector4(shadowCascadeData.offsetX * invShadowmapWidth, shadowCascadeData.offsetY * invShadowmapHeight, shadowCascadeData.resolution * invShadowmapWidth, shadowCascadeData.resolution * invShadowmapHeight);

                Matrix4x4 deviceProjectionMatrix = GL.GetGPUProjectionMatrix(shadowCascadeData.projectionMatrix, false);
                m_DeviceProjectionVectors[cascadeIndex] = new Vector4(deviceProjectionMatrix.m00, deviceProjectionMatrix.m11, deviceProjectionMatrix.m22, deviceProjectionMatrix.m23);

                float shadowmapDepth2RadialScale = Mathf.Abs(deviceProjectionMatrix.m00 / deviceProjectionMatrix.m22);

                m_DirLightPCSSParams0[cascadeIndex] = new Vector4(
                    dirLightDepth2Radius * shadowmapDepth2RadialScale,  // depth2RadialScale
                    1.0f / (dirLightDepth2Radius * shadowmapDepth2RadialScale),  // radial2DepthScale
                    PCSSLightParams.dirLightPcssMaxPenumbraSize / (2.0f * halfMinFilterAngularDiameterTangent),  // maxBlockerDistance
                    PCSSLightParams.dirLightPcssMaxSamplingDistance  // maxSamplingDistance
                );

                m_DirLightPCSSParams1[cascadeIndex] = new Vector4(
                    PCSSLightParams.dirLightPcssMinFilterSizeTexels,  // minFilterRadius(in texel size)
                    1.0f / (halfMinFilterAngularDiameterTangent * shadowmapDepth2RadialScale),  // minFilterRadial2DepthScale
                    1.0f / (halfBlockerSearchAngularDiameterTangent * shadowmapDepth2RadialScale),  // blockerRadial2DepthScale
                    0.5f * PCSSLightParams.dirLightPcssBlockerSamplingClumpExponent  // blockerClumpSampleExponent
                );
            }
        }

        // We setup and additional a no-op WorldToShadow matrix in the last index
        // because the ComputeCascadeIndex function in Shadows.hlsl can return an index
        // out of bounds. (position not inside any cascade) and we want to avoid branching
        Matrix4x4 noOpShadowMatrix = Matrix4x4.zero;
        noOpShadowMatrix.m22 = (SystemInfo.usesReversedZBuffer) ? 1.0f : 0.0f;
        for (int i = m_ShadowCasterCascadesCount; i <= k_MaxCascades; ++i)
        {
            m_MainLightShadowMatrices[i] = noOpShadowMatrix;

            m_CascadeOffsetScales[i] = new Vector4(0.0f, 0.0f, 1.0f, 1.0f);
            m_DeviceProjectionVectors[i] = Vector4.one;
            m_DirLightPCSSParams0[i] = Vector4.zero;
            m_DirLightPCSSParams1[i] = Vector4.zero;
        }

        ShadowUtils.GetScaleAndBiasForLinearDistanceFade(m_MaxShadowDistanceSq, m_CascadeBorder, out float shadowFadeScale, out float shadowFadeBias);

        cmd.SetGlobalMatrixArray(MainLightShadowConstantBuffer._MainLightWorldToShadow, m_MainLightShadowMatrices);
        cmd.SetGlobalVector(MainLightShadowConstantBuffer._ShadowParams, new Vector4(shadowLight.light.shadowStrength, 0.0f, shadowFadeScale, shadowFadeBias));
        cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadesParams, new Vector4((float)m_ShadowCasterCascadesCount, 0.0f, 0.0f, 0.0f));

        if (m_ShadowCasterCascadesCount > 1)
        {
            cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadeShadowSplitSpheres0, m_CascadesSplitDistance[0]);
            cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadeShadowSplitSpheres1, m_CascadesSplitDistance[1]);
            cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadeShadowSplitSpheres2, m_CascadesSplitDistance[2]);
            cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadeShadowSplitSpheres3, m_CascadesSplitDistance[3]);
        }

        if (renderingData.shadowData.softShadows == SoftShadows.PCSS)
        {
            cmd.SetGlobalVectorArray(MainLightShadowConstantBuffer._CascadeOffsetScales, m_CascadeOffsetScales);
            cmd.SetGlobalVectorArray(MainLightShadowConstantBuffer._DirLightPCSSParams0, m_DirLightPCSSParams0);
            cmd.SetGlobalVectorArray(MainLightShadowConstantBuffer._DirLightPCSSParams1, m_DirLightPCSSParams1);
            cmd.SetGlobalVectorArray(MainLightShadowConstantBuffer._DirLightPCSSProjs, m_DeviceProjectionVectors);
        }
    }

    private void SetEmptyMainLightCascadeShadowmap(RasterCommandBuffer cmd)
    {
        cmd.SetGlobalVector(MainLightShadowConstantBuffer._ShadowParams, new Vector4(1, 0, 1, 0));
        cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadesParams, new Vector4(1, 0, 0, 0));
    }

    private bool SetupForEmptyRendering()
    {
        m_CreateEmptyShadowmap = true;

        ShadowUtils.ShadowRTReAllocateIfNeeded(ref m_EmptyLightShadowmapTexture, 1, 1, k_ShadowmapBufferBits, name: "_EmptyLightShadowmapTexture");

        return true;
    }

    private void Clear()
    {
        for (int i = 0; i < m_MainLightShadowMatrices.Length; ++i)
            m_MainLightShadowMatrices[i] = Matrix4x4.identity;

        for (int i = 0; i < m_CascadesSplitDistance.Length; ++i)
            m_CascadesSplitDistance[i] = Vector4.zero;

        for (int i = 0; i < m_CascadeOffsetScales.Length; ++i)
            m_CascadeOffsetScales[i] = Vector4.zero;

        for (int i = 0; i < m_DirLightPCSSParams0.Length; ++i)
        {
            m_DirLightPCSSParams0[i] = Vector4.zero;
            m_DirLightPCSSParams1[i] = Vector4.zero;
        }

        for (int i = 0; i < m_DeviceProjectionVectors.Length; ++i)
        {
            m_DeviceProjectionVectors[i] = Vector4.zero;
        }
    }
}
