using UnityEngine;
using UnityEngine.Rendering;

public class MainLightShadowPass
{
    private int m_MainLightShadowmapID;
    private RTHandle m_MainLightShadowmapHandle;
    private RTHandle m_EmptyLightShadowmapHandle;

    // This limit matches same limit in Shadows.hlsl
    private const int k_MaxCascades = 4;

    private const int k_ShadowmapBufferBits = 16;
    private const string k_ShadowmapTextureName = "_MainLightShadowmapTexture";

    private Matrix4x4[] m_MainLightShadowMatrices;
    private Vector4[] m_CascadesSplitDistance;

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
        MainLightShadowConstantBuffer._CascadeShadowSplitSphereRadii = Shader.PropertyToID("_CascadeShadowSplitSphereRadii");

        m_MainLightShadowmapID = Shader.PropertyToID(k_ShadowmapTextureName);

        m_EmptyLightShadowmapHandle = ShadowUtils.AllocShadowRT(1, 1, k_ShadowmapBufferBits, "_EmptyLightShadowmapTexture");
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

        int shadowmapWidth = shadowData.mainLightShadowmapWidth;
        int shadowmapHeight = shadowData.mainLightShadowmapHeight;
        ShadowUtils.ShadowRTReAllocateIfNeeded(ref m_MainLightShadowmapHandle, shadowmapWidth, shadowmapHeight, k_ShadowmapBufferBits, name: k_ShadowmapTextureName);

        m_MaxShadowDistanceSq = shadowData.maxShadowDistance * shadowData.maxShadowDistance;
        m_CascadeBorder = shadowData.mainLightShadowCascadeBorder;

        return true;
    }

    public void Render(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cmd = renderingData.commandBuffer;

        // Setup render target and clear target
        cmd.SetRenderTarget(m_MainLightShadowmapHandle, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
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

            int cascadeResolution = ShadowUtils.GetMaxTileResolutionInAtlas(renderingData.shadowData.mainLightShadowmapWidth, renderingData.shadowData.mainLightShadowmapHeight, cascadesCount);

            int renderTargetWidth = renderingData.shadowData.mainLightShadowmapWidth;
            int renderTargetHeight = (cascadesCount == 2) ? renderingData.shadowData.mainLightShadowmapHeight >> 1 : renderingData.shadowData.mainLightShadowmapHeight;

            for (int i = 0; i < cascadesCount; ++i)
            {
                ShadowUtils.ExtractDirectionalLightMatrix(ref cullResults, shadowLightIndex, i, cascadesCount, cascadesSplit,
                    renderTargetWidth, renderTargetHeight, cascadeResolution, shadowLight.light.shadowNearPlane, out ShadowCascadeData shadowCascadeData);

                shadowDrawingSettings.splitData = shadowCascadeData.splitData;

                Vector4 shadowBias = ShadowUtils.GetShadowBias(shadowLight, shadowLightIndex, shadowCascadeData.projectionMatrix, shadowCascadeData.resolution);
                ShadowUtils.SetupShadowCasterConstantBuffer(cmd, shadowLight, shadowBias);

                cmd.SetViewport(new Rect(shadowCascadeData.offsetX, shadowCascadeData.offsetY, shadowCascadeData.resolution, shadowCascadeData.resolution));
                cmd.SetViewProjectionMatrices(shadowCascadeData.viewMatrix, shadowCascadeData.projectionMatrix);

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                m_MainLightShadowMatrices[i] = shadowCascadeData.shadowTransform;
                m_CascadesSplitDistance[i] = shadowCascadeData.splitData.cullingSphere;

                context.DrawShadows(ref shadowDrawingSettings);
            }

            // We setup and additional a no-op WorldToShadow matrix in the last index
            // because the ComputeCascadeIndex function in Shadows.hlsl can return an index
            // out of bounds. (position not inside any cascade) and we want to avoid branching
            Matrix4x4 noOpShadowMatrix = Matrix4x4.zero;
            noOpShadowMatrix.m22 = (SystemInfo.usesReversedZBuffer) ? 1.0f : 0.0f;
            for (int i = cascadesCount; i <= k_MaxCascades; ++i)
                m_MainLightShadowMatrices[i] = noOpShadowMatrix;

            cmd.SetGlobalTexture(m_MainLightShadowmapID, m_MainLightShadowmapHandle.nameID);

            ShadowUtils.GetScaleAndBiasForLinearDistanceFade(m_MaxShadowDistanceSq, m_CascadeBorder, out float shadowFadeScale, out float shadowFadeBias);

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
        m_MainLightShadowmapHandle?.Release();
        m_EmptyLightShadowmapHandle?.Release();
    }

    private bool SetupForEmptyRendering(ref RenderingData renderingData)
    {
        var cmd = renderingData.commandBuffer;
        var context = renderingData.renderContext;

        ShadowUtils.ShadowRTReAllocateIfNeeded(ref m_EmptyLightShadowmapHandle, 1, 1, k_ShadowmapBufferBits, name: "_EmptyLightShadowmapTexture");
        cmd.SetGlobalTexture(m_MainLightShadowmapID, m_EmptyLightShadowmapHandle.nameID);

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        return false;
    }

    private void Clear()
    {
        for (int i = 0; i < m_MainLightShadowMatrices.Length; ++i)
            m_MainLightShadowMatrices[i] = Matrix4x4.identity;

        for (int i = 0; i < m_CascadesSplitDistance.Length; ++i)
            m_CascadesSplitDistance[i] = Vector4.zero;
    }
}
