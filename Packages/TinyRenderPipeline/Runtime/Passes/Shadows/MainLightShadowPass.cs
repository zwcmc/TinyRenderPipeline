using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class MainLightShadowPass
{
    private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler("MainLight ShadowMap");
    private static readonly ProfilingSampler s_SetMainLightShadowSampler = new ProfilingSampler("SetMainLightShadowMapGlobal");

    // This limit matches same limit in Shadows.hlsl
    private const int k_MaxCascades = 4;
    private const int k_ShadowMapBufferBits = 16;
    private const string k_ShadowMapTextureName = "_MainLightShadowMapTexture";

    private int m_MainLightShadowMapID;
    private RTHandle m_MainLightShadowMapTexture;
    private RTHandle m_EmptyLightShadowMapTexture;

    private bool m_CreateEmptyShadowMap;

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

        public TextureHandle shadowMapTexture;
        public RenderingData renderingData;

        public int shadowMapID;
        public bool emptyShadowMap;

        public RendererListHandle[] shadowRendererListHandle = new RendererListHandle[k_MaxCascades];
    }

    private Vector4[] m_DirLightPCSSParams0;
    private Vector4[] m_DirLightPCSSParams1;

    private static class PCSSLightParams
    {
        public static float dirLightAngularDiameter = 2.66f;
        public static float dirLightPCSSBlockerSearchAngularDiameter = 12;
        public static float dirLightPCSSMinFilterMaxAngularDiameter = 10;
        public static float dirLightPCSSMaxPenumbraSize = 0.56f;
        public static float dirLightPCSSMaxSamplingDistance = 0.5f;
        public static float dirLightPCSSMinFilterSizeTexels = 1.5f;
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

        m_MainLightShadowMapID = Shader.PropertyToID(k_ShadowMapTextureName);

        m_EmptyLightShadowMapTexture = ShadowUtils.AllocShadowRT(1, 1, k_ShadowMapBufferBits, "_EmptyLightShadowMapTexture");
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
        m_RenderTargetWidth = renderingData.shadowData.mainLightShadowMapWidth;
        m_RenderTargetHeight = (m_ShadowCasterCascadesCount == 2) ? renderingData.shadowData.mainLightShadowMapHeight >> 1 : renderingData.shadowData.mainLightShadowMapHeight;
        ShadowUtils.ShadowRTReAllocateIfNeeded(ref m_MainLightShadowMapTexture, m_RenderTargetWidth, m_RenderTargetHeight, k_ShadowMapBufferBits, name: k_ShadowMapTextureName);

        m_MaxShadowDistanceSq = shadowData.maxShadowDistance * shadowData.maxShadowDistance;
        m_CascadeBorder = shadowData.mainLightShadowCascadeBorder;

        m_CreateEmptyShadowMap = false;

        return true;
    }

    public TextureHandle RecordRenderGraph(RenderGraph renderGraph, ref RenderingData renderingData)
    {
        TextureHandle shadowTexture;

        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_ProfilingSampler.name, out var passData, s_ProfilingSampler))
        {
            InitPassData(renderGraph, ref renderingData, ref passData);

            if (!m_CreateEmptyShadowMap)
            {
                for (int cascadeIndex = 0; cascadeIndex < m_ShadowCasterCascadesCount; ++cascadeIndex)
                    builder.UseRendererList(passData.shadowRendererListHandle[cascadeIndex]);

                passData.shadowMapTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, m_MainLightShadowMapTexture.rt.descriptor, k_ShadowMapTextureName, false, FilterMode.Bilinear);
                builder.UseTextureFragmentDepth(passData.shadowMapTexture, IBaseRenderGraphBuilder.AccessFlags.Write);
            }

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext rasterGraphContext) =>
            {
                if (!data.emptyShadowMap)
                    data.pass.RenderMainLightCascadeShadowMap(rasterGraphContext.cmd, ref data, ref data.renderingData);
            });

            shadowTexture = passData.shadowMapTexture;
        }

        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_SetMainLightShadowSampler.name, out var passData, s_SetMainLightShadowSampler))
        {
            passData.pass = this;
            passData.shadowMapID = m_MainLightShadowMapID;
            passData.emptyShadowMap = m_CreateEmptyShadowMap;
            passData.renderingData = renderingData;

            passData.shadowMapTexture = shadowTexture;

            if (shadowTexture.IsValid())
                builder.UseTexture(shadowTexture, IBaseRenderGraphBuilder.AccessFlags.Read);

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext rasterGraphContext) =>
            {
                if (data.emptyShadowMap)
                {
                    data.pass.SetEmptyMainLightCascadeShadowMap(rasterGraphContext.cmd);
                    data.shadowMapTexture = rasterGraphContext.defaultResources.defaultShadowTexture;
                }

                rasterGraphContext.cmd.SetGlobalTexture(data.shadowMapID, data.shadowMapTexture);
            });
            return passData.shadowMapTexture;
        }
    }

    public void Dispose()
    {
        m_MainLightShadowMapTexture?.Release();
        m_EmptyLightShadowMapTexture?.Release();
    }

    private void InitPassData(RenderGraph renderGraph, ref RenderingData renderingData, ref PassData passData)
    {
        passData.pass = this;
        passData.emptyShadowMap = m_CreateEmptyShadowMap;
        passData.shadowMapID = m_MainLightShadowMapID;
        passData.renderingData = renderingData;

        int shadowLightIndex = renderingData.mainLightIndex;
        if (!m_CreateEmptyShadowMap && shadowLightIndex != -1)
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

    private void RenderMainLightCascadeShadowMap(RasterCommandBuffer cmd, ref PassData data, ref RenderingData renderingData)
    {
        int shadowLightIndex = renderingData.mainLightIndex;
        if (shadowLightIndex == -1)
            return;

        ref var cullResults = ref renderingData.cullResults;
        VisibleLight shadowLight = cullResults.visibleLights[shadowLightIndex];

        cmd.SetGlobalVector(ShaderPropertyID.worldSpaceCameraPos, renderingData.camera.transform.position);

        Vector3 cascadesSplit = renderingData.shadowData.cascadesSplit;
        int cascadeResolution = ShadowUtils.GetMaxTileResolutionInAtlas(renderingData.shadowData.mainLightShadowMapWidth, renderingData.shadowData.mainLightShadowMapHeight, m_ShadowCasterCascadesCount);

        // PCSS
        float invShadowMapWidth = default;
        float invShadowMapHeight = default;
        float dirLightDepth2Radius = default;
        float halfMinFilterAngularDiameterTangent = default;
        float halfBlockerSearchAngularDiameterTangent = default;
        if (renderingData.shadowData.softShadows == SoftShadows.PCSS)
        {
            // ShadowMapAtlas 中一个纹素的宽度
            invShadowMapWidth = 1.0f / m_RenderTargetWidth;
            // ShadowMapAtlas 中一个纹素的高度
            invShadowMapHeight = 1.0f / m_RenderTargetHeight;
            // 控制参数：方向光角直径 \delta
            float lightAngularDiameter = PCSSLightParams.dirLightAngularDiameter;
            // 根据角直径参数, 计算出光源直径 d 与 距离光源的距离 D 的关系, 即 tan(\delta / 2) = d_{dirLight} / 2D
            dirLightDepth2Radius = Mathf.Tan(0.5f * Mathf.Deg2Rad * lightAngularDiameter);

            float minFilterAngularDiameter = Mathf.Max(PCSSLightParams.dirLightPCSSBlockerSearchAngularDiameter, PCSSLightParams.dirLightPCSSMinFilterMaxAngularDiameter);
            // 控制参数：Filtering 时, Filtering 的范围直径 d 与距离光源距离的 D 的关系 , d_{Filtering} / 2D
            halfMinFilterAngularDiameterTangent = Mathf.Tan(0.5f * Mathf.Deg2Rad * Mathf.Max(minFilterAngularDiameter, lightAngularDiameter));
            // 控制参数：Blocker 搜索时, 搜索的范围直径 d 与距离光源距离 D 的关系: d_{Blocker} / 2D
            halfBlockerSearchAngularDiameterTangent = Mathf.Tan(0.5f * Mathf.Deg2Rad * Mathf.Max(PCSSLightParams.dirLightPCSSBlockerSearchAngularDiameter, lightAngularDiameter));
        }

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
                // 每一个 Cascade 在 ShadowMapAtlas 上的起始 uv 坐标与 uv 范围
                m_CascadeOffsetScales[cascadeIndex] = new Vector4(shadowCascadeData.offsetX * invShadowMapWidth, shadowCascadeData.offsetY * invShadowMapHeight, shadowCascadeData.resolution * invShadowMapWidth, shadowCascadeData.resolution * invShadowMapHeight);

                // Cascade 的投影矩阵
                Matrix4x4 deviceProjectionMatrix = GL.GetGPUProjectionMatrix(shadowCascadeData.projectionMatrix, false);

                // x: 光源空间投影矩阵 x 轴的缩放
                // y: 光源空间投影矩阵 y 轴的缩放
                // z: 光源空间投影矩阵 z 轴的缩放
                // w: 光源空间投影矩阵 z 轴的位移
                m_DeviceProjectionVectors[cascadeIndex] = new Vector4(deviceProjectionMatrix.m00, deviceProjectionMatrix.m11, deviceProjectionMatrix.m22, deviceProjectionMatrix.m23);

                // 计算出 Cascade 投影矩阵中的 x 轴和 z 轴的缩放比例, 考虑到光源投影矩阵中 x 轴与 z 轴的非均匀缩放
                float shadowMapDepth2RadialScale = Mathf.Abs(deviceProjectionMatrix.m00 / deviceProjectionMatrix.m22);

                m_DirLightPCSSParams0[cascadeIndex] = new Vector4(
                    dirLightDepth2Radius * shadowMapDepth2RadialScale,  // x = depth2RadialScale , 方向光源直角径中, 光源直径 d_{dirLight} 与距离光源距离 D 的比例关系: d_{dirLight} / 2D
                    0.0f,
                    PCSSLightParams.dirLightPCSSMaxPenumbraSize / (2.0f * halfMinFilterAngularDiameterTangent),  // z = maxBlockerDistance , 根据自定义参数 PCSSLightParams.dirLightPCSSMaxPenumbraSize , 计算出的最大 Blocker 搜索的深度
                    PCSSLightParams.dirLightPCSSMaxSamplingDistance  // w = maxSamplingDistance , 自定义参数 最大搜索深度
                );

                m_DirLightPCSSParams1[cascadeIndex] = new Vector4(
                    PCSSLightParams.dirLightPCSSMinFilterSizeTexels,  // x = minFilterRadius(in texel size) , 最小的搜索范围，此范围是相对于纹素宽度的范围 , 默认是 1.5 , 也就是 1.5 个纹素宽度
                    1.0f / (halfMinFilterAngularDiameterTangent * shadowMapDepth2RadialScale),  // y = minFilterRadial2DepthScale , Filtering 时, 距离光源距离 D 与 Filtering 的范围直径 d 的关系: 2D / d_{Filtering}
                    1.0f / (halfBlockerSearchAngularDiameterTangent * shadowMapDepth2RadialScale),  // z = blockerRadial2DepthScale , Blocker 搜索时, 距离光源距离 D 与搜索的范围直径 d 的关系: 2D / d_{Blocker}
                    0.0f
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

    private void SetEmptyMainLightCascadeShadowMap(RasterCommandBuffer cmd)
    {
        cmd.SetGlobalVector(MainLightShadowConstantBuffer._ShadowParams, new Vector4(1, 0, 1, 0));
        cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadesParams, new Vector4(1, 0, 0, 0));
    }

    private bool SetupForEmptyRendering()
    {
        m_CreateEmptyShadowMap = true;

        ShadowUtils.ShadowRTReAllocateIfNeeded(ref m_EmptyLightShadowMapTexture, 1, 1, k_ShadowMapBufferBits, name: "_EmptyLightShadowMapTexture");

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
