using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class MainLightShadowPass
{
    private int m_MainLightShadowmapID;
    private RTHandle m_MainLightShadowmapTexture;
    private RTHandle m_EmptyLightShadowmapTexture;

    // This limit matches same limit in Shadows.hlsl
    private const int k_MaxCascades = 4;
    private const int k_ShadowmapBufferBits = 16;
    private const string k_ShadowmapTextureName = "_MainLightShadowmapTexture";

    private bool m_CreateEmptyShadowmap;
    bool m_EmptyShadowmapNeedsClear = false;

    private Matrix4x4[] m_MainLightShadowMatrices;
    private Vector4[] m_CascadesSplitDistance;

    private float m_CascadeBorder;
    private float m_MaxShadowDistanceSq;
    private int m_ShadowCasterCascadesCount;
    private int m_RenderTargetWidth;
    private int m_RenderTargetHeight;

    private PassData m_PassData;

    private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler("MainLightShadowPass");
    private static readonly ProfilingSampler s_SetMainLightShadowSampler = new ProfilingSampler("SetMainLightShadowmapGlobal");

    private static class MainLightShadowConstantBuffer
    {
        public static int _WorldToShadow;
        public static int _ShadowParams;
        public static int _CascadeShadowSplitSpheres0;
        public static int _CascadeShadowSplitSpheres1;
        public static int _CascadeShadowSplitSpheres2;
        public static int _CascadeShadowSplitSpheres3;
        public static int _CascadesParams;
    }

    public MainLightShadowPass()
    {
        m_MainLightShadowMatrices = new Matrix4x4[k_MaxCascades + 1];
        m_CascadesSplitDistance = new Vector4[k_MaxCascades];

        MainLightShadowConstantBuffer._WorldToShadow = Shader.PropertyToID("_MainLightWorldToShadow");
        MainLightShadowConstantBuffer._ShadowParams = Shader.PropertyToID("_MainLightShadowParams");
        MainLightShadowConstantBuffer._CascadeShadowSplitSpheres0 = Shader.PropertyToID("_CascadeShadowSplitSpheres0");
        MainLightShadowConstantBuffer._CascadeShadowSplitSpheres1 = Shader.PropertyToID("_CascadeShadowSplitSpheres1");
        MainLightShadowConstantBuffer._CascadeShadowSplitSpheres2 = Shader.PropertyToID("_CascadeShadowSplitSpheres2");
        MainLightShadowConstantBuffer._CascadeShadowSplitSpheres3 = Shader.PropertyToID("_CascadeShadowSplitSpheres3");
        MainLightShadowConstantBuffer._CascadesParams = Shader.PropertyToID("_CascadesParams");

        m_MainLightShadowmapID = Shader.PropertyToID(k_ShadowmapTextureName);

        m_EmptyLightShadowmapTexture = ShadowUtils.AllocShadowRT(1, 1, k_ShadowmapBufferBits, "_EmptyLightShadowmapTexture");
        m_EmptyShadowmapNeedsClear = true;

        m_PassData = new PassData();
    }

    public bool Setup(ref RenderingData renderingData)
    {
        if (!renderingData.shadowData.mainLightShadowsEnabled)
        {
            return SetupForEmptyRendering(ref renderingData);
        }

        int mainLightIndex = renderingData.mainLightIndex;
        if (mainLightIndex == -1)
        {
            return SetupForEmptyRendering(ref renderingData);
        }

        VisibleLight shadowLight = renderingData.cullResults.visibleLights[mainLightIndex];
        // Main light is always a directional light
        if (shadowLight.lightType != LightType.Directional)
        {
            return SetupForEmptyRendering(ref renderingData);
        }

        // Check light's shadow settings
        Light light = shadowLight.light;
        if (light.shadows == LightShadows.None || light.shadowStrength <= 0f)
        {
            return SetupForEmptyRendering(ref renderingData);
        }

        // Check if the light affects as least one shadow casting object in scene
        if (!renderingData.cullResults.GetShadowCasterBounds(mainLightIndex, out Bounds bounds))
        {
            return SetupForEmptyRendering(ref renderingData);
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

    public void Render(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cmd = renderingData.commandBuffer;

        if (m_CreateEmptyShadowmap)
        {
            SetEmptyMainLightCascadeShadowmap(CommandBufferHelpers.GetRasterCommandBuffer(cmd));

            cmd.SetRenderTarget(m_EmptyLightShadowmapTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.ClearRenderTarget(true, true, Color.clear);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            renderingData.commandBuffer.SetGlobalTexture(m_MainLightShadowmapID, m_EmptyLightShadowmapTexture.nameID);
            return;
        }

        // Setup render target and clear target
        cmd.SetRenderTarget(m_MainLightShadowmapTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
        cmd.ClearRenderTarget(true, true, Color.clear);

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        using (new ProfilingScope(cmd, s_ProfilingSampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            InitializePassData(context, default(RenderGraph), ref renderingData, ref m_PassData, false);

            RenderMainLightCascadeShadowmap(CommandBufferHelpers.GetRasterCommandBuffer(cmd), ref m_PassData, ref renderingData, false);

            cmd.SetGlobalTexture(m_MainLightShadowmapID, m_MainLightShadowmapTexture.nameID);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
    }

    private void InitializePassData(ScriptableRenderContext context, RenderGraph renderGraph, ref RenderingData renderingData, ref PassData passData, bool useRenderGraph)
    {
        passData.pass = this;
        passData.emptyShadowmap = m_CreateEmptyShadowmap;
        passData.shadowmapID = m_MainLightShadowmapID;
        passData.renderingData = renderingData;

        var cullResults = renderingData.cullResults;
        int shadowLightIndex = renderingData.mainLightIndex;
        if (!m_CreateEmptyShadowmap && shadowLightIndex != -1)
        {
            var settings = new ShadowDrawingSettings(cullResults, shadowLightIndex);
            settings.useRenderingLayerMaskTest = true;
            for (int cascadeIndex = 0; cascadeIndex < m_ShadowCasterCascadesCount; ++cascadeIndex)
            {
                if (useRenderGraph)
                    passData.shadowRendererListHandle[cascadeIndex] = renderGraph.CreateShadowRendererList(ref settings);
                else
                    passData.shadowRendererLists[cascadeIndex] = context.CreateShadowRendererList(ref settings);
            }
        }
    }

    private void RenderMainLightCascadeShadowmap(RasterCommandBuffer cmd, ref PassData data, ref RenderingData renderingData, bool useRenderGraph)
    {
        int shadowLightIndex = renderingData.mainLightIndex;
        if (shadowLightIndex == -1)
            return;

        ref var cullResults = ref renderingData.cullResults;
        VisibleLight shadowLight = cullResults.visibleLights[shadowLightIndex];

        cmd.SetGlobalVector(ShaderPropertyId.worldSpaceCameraPos, renderingData.camera.transform.position);

        Vector3 cascadesSplit = renderingData.shadowData.cascadesSplit;
        int cascadeResolution = ShadowUtils.GetMaxTileResolutionInAtlas(renderingData.shadowData.mainLightShadowmapWidth, renderingData.shadowData.mainLightShadowmapHeight, m_ShadowCasterCascadesCount);

        for (int cascadeIndex = 0; cascadeIndex < m_ShadowCasterCascadesCount; ++cascadeIndex)
        {
            bool success = ShadowUtils.ExtractDirectionalLightMatrix(ref cullResults, shadowLightIndex, cascadeIndex, m_ShadowCasterCascadesCount, cascadesSplit,
                m_RenderTargetWidth, m_RenderTargetHeight, cascadeResolution, shadowLight.light.shadowNearPlane, out ShadowSliceData shadowCascadeData);

            // This cascade does not need to be rendered this frame
            if (!success)
                continue;

            Vector4 shadowBias = ShadowUtils.GetShadowBias(shadowLight, shadowLightIndex, shadowCascadeData.projectionMatrix, shadowCascadeData.resolution);
            ShadowUtils.SetupShadowCasterConstantBuffer(cmd, shadowLight, shadowBias);

            cmd.SetViewport(new Rect(shadowCascadeData.offsetX, shadowCascadeData.offsetY, shadowCascadeData.resolution, shadowCascadeData.resolution));
            cmd.SetViewProjectionMatrices(shadowCascadeData.viewMatrix, shadowCascadeData.projectionMatrix);

            RendererList shadowRendererList = useRenderGraph ? data.shadowRendererListHandle[cascadeIndex] : data.shadowRendererLists[cascadeIndex];
            if (shadowRendererList.isValid)
                cmd.DrawRendererList(shadowRendererList);

            m_MainLightShadowMatrices[cascadeIndex] = shadowCascadeData.shadowTransform;
            Vector4 cullingSphere = shadowCascadeData.splitData.cullingSphere;
            cullingSphere.w *= cullingSphere.w;
            m_CascadesSplitDistance[cascadeIndex] = cullingSphere;
        }

        // We setup and additional a no-op WorldToShadow matrix in the last index
        // because the ComputeCascadeIndex function in Shadows.hlsl can return an index
        // out of bounds. (position not inside any cascade) and we want to avoid branching
        Matrix4x4 noOpShadowMatrix = Matrix4x4.zero;
        noOpShadowMatrix.m22 = (SystemInfo.usesReversedZBuffer) ? 1.0f : 0.0f;
        for (int i = m_ShadowCasterCascadesCount; i <= k_MaxCascades; ++i)
            m_MainLightShadowMatrices[i] = noOpShadowMatrix;

        ShadowUtils.GetScaleAndBiasForLinearDistanceFade(m_MaxShadowDistanceSq, m_CascadeBorder, out float shadowFadeScale, out float shadowFadeBias);

        cmd.SetGlobalMatrixArray(MainLightShadowConstantBuffer._WorldToShadow, m_MainLightShadowMatrices);
        cmd.SetGlobalVector(MainLightShadowConstantBuffer._ShadowParams, new Vector4(shadowLight.light.shadowStrength, 0.0f, shadowFadeScale, shadowFadeBias));
        cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadesParams, new Vector4((float)m_ShadowCasterCascadesCount, 0.0f, 0.0f, 0.0f));

        if (m_ShadowCasterCascadesCount > 1)
        {
            cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadeShadowSplitSpheres0, m_CascadesSplitDistance[0]);
            cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadeShadowSplitSpheres1, m_CascadesSplitDistance[1]);
            cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadeShadowSplitSpheres2, m_CascadesSplitDistance[2]);
            cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadeShadowSplitSpheres3, m_CascadesSplitDistance[3]);
        }
    }

    private class PassData
    {
        public MainLightShadowPass pass;

        public TextureHandle shadowmapTexture;
        public RenderingData renderingData;

        public int shadowmapID;
        public bool emptyShadowmap;

        public RendererListHandle[] shadowRendererListHandle = new RendererListHandle[k_MaxCascades];
        public RendererList[] shadowRendererLists = new RendererList[k_MaxCascades];
    }

    public TextureHandle RenderGraphRender(RenderGraph renderGraph, ref RenderingData renderingData)
    {
        TextureHandle shadowTexture;

        using (var builder = renderGraph.AddRasterRenderPass<PassData>(s_ProfilingSampler.name, out var passData, s_ProfilingSampler))
        {
            InitializePassData(default(ScriptableRenderContext), renderGraph, ref renderingData, ref passData, true);

            if (!m_CreateEmptyShadowmap)
            {
                for (int cascadeIndex = 0; cascadeIndex < m_ShadowCasterCascadesCount; ++cascadeIndex)
                    builder.UseRendererList(passData.shadowRendererListHandle[cascadeIndex]);

                passData.shadowmapTexture = RenderingUtils.CreateRenderGraphTexture(renderGraph, m_MainLightShadowmapTexture.rt.descriptor, "_MainLightShadowmapTexture", true, FilterMode.Bilinear);
                builder.UseTextureFragmentDepth(passData.shadowmapTexture, IBaseRenderGraphBuilder.AccessFlags.Write);
            }

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext rasterGraphContext) =>
            {
                if (!data.emptyShadowmap)
                    data.pass.RenderMainLightCascadeShadowmap(rasterGraphContext.cmd, ref data, ref data.renderingData, true);
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

    private void SetEmptyMainLightCascadeShadowmap(RasterCommandBuffer cmd)
    {
        cmd.SetGlobalVector(MainLightShadowConstantBuffer._ShadowParams, new Vector4(1, 0, 1, 0));
        cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadesParams, new Vector4(1, 0, 0, 0));
    }

    private bool SetupForEmptyRendering(ref RenderingData renderingData)
    {
        m_CreateEmptyShadowmap = true;

        if (ShadowUtils.ShadowRTReAllocateIfNeeded(ref m_EmptyLightShadowmapTexture, 1, 1, k_ShadowmapBufferBits, name: "_EmptyLightShadowmapTexture"))
            m_EmptyShadowmapNeedsClear = true;

        return true;
    }

    private void Clear()
    {
        for (int i = 0; i < m_MainLightShadowMatrices.Length; ++i)
            m_MainLightShadowMatrices[i] = Matrix4x4.identity;

        for (int i = 0; i < m_CascadesSplitDistance.Length; ++i)
            m_CascadesSplitDistance[i] = Vector4.zero;
    }
}
