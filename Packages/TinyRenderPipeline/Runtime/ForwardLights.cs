using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class ForwardLights
{
    private static readonly ProfilingSampler s_SetupLightsSampler = new ProfilingSampler("SetupForwardLights");
    private static readonly ProfilingSampler m_ProfilingSamplerFPUpload = new ProfilingSampler("Forward+ Upload");

    private static class LightDefaultValue
    {
        public static Vector4 DefaultLightPosition = new Vector4(0.0f, 0.0f, 1.0f, 0.0f);
        public static Vector4 DefaultLightColor = Color.black;
        public static Vector4 DefaultLightAttenuation = new Vector4(0.0f, 1.0f, 0.0f, 1.0f);
        public static Vector4 DefaultLightSpotDirection = new Vector4(0.0f, 0.0f, 1.0f, 0.0f);
    }

    private static class LightConstantBuffer
    {
        public static int _MainLightPosition;
        public static int _MainLightColor;
        public static int _MainLightLayerMask;

        public static int _AdditionalLightsCount;
        public static int _AdditionalLightsPosition;
        public static int _AdditionalLightsColor;
        public static int _AdditionalLightsAttenuation;
        public static int _AdditionalLightsSpotDir;
        public static int _AdditionalLightsLayerMasks;
    }

    private Vector4[] m_AdditionalLightPositions;
    private Vector4[] m_AdditionalLightColors;
    private Vector4[] m_AdditionalLightAttenuations;
    private Vector4[] m_AdditionalLightSpotDirections;
    private float[] m_AdditionalLightsLayerMasks;  // Unity has no support for binding uint arrays. We will use asuint() in the shader instead.

    private class SetupLightsPassData
    {
        public RenderingData renderingData;
        public ForwardLights forwardLights;
    }

    private bool m_UseForwardPlus;
    private JobHandle m_CullingHandle;
    NativeArray<uint> m_ZBins;
    GraphicsBuffer m_ZBinsBuffer;
    NativeArray<uint> m_TileMasks;
    GraphicsBuffer m_TileMasksBuffer;

    private int m_LightCount;
    private int m_DirectionalLightCount;

    public ForwardLights(bool isForwardPlusRenderingPath = false)
    {
        m_UseForwardPlus = isForwardPlusRenderingPath;

        LightConstantBuffer._MainLightPosition = Shader.PropertyToID("_MainLightPosition");
        LightConstantBuffer._MainLightColor = Shader.PropertyToID("_MainLightColor");
        LightConstantBuffer._MainLightLayerMask = Shader.PropertyToID("_MainLightLayerMask");

        LightConstantBuffer._AdditionalLightsCount = Shader.PropertyToID("_AdditionalLightsCount");
        LightConstantBuffer._AdditionalLightsPosition = Shader.PropertyToID("_AdditionalLightsPosition");
        LightConstantBuffer._AdditionalLightsColor = Shader.PropertyToID("_AdditionalLightsColor");
        LightConstantBuffer._AdditionalLightsAttenuation = Shader.PropertyToID("_AdditionalLightsAttenuation");
        LightConstantBuffer._AdditionalLightsSpotDir = Shader.PropertyToID("_AdditionalLightsSpotDir");
        LightConstantBuffer._AdditionalLightsLayerMasks = Shader.PropertyToID("_AdditionalLightsLayerMasks");

        int maxAdditionalLights = TinyRenderPipeline.maxVisibleAdditionalLights;
        m_AdditionalLightPositions = new Vector4[maxAdditionalLights];
        m_AdditionalLightColors = new Vector4[maxAdditionalLights];
        m_AdditionalLightAttenuations = new Vector4[maxAdditionalLights];
        m_AdditionalLightSpotDirections = new Vector4[maxAdditionalLights];
        m_AdditionalLightsLayerMasks = new float[maxAdditionalLights];

        // Forward+ rendering path
        if (m_UseForwardPlus)
        {
            CreateForwardPlusBuffers();
        }
    }

    public void PreSetup(ref RenderingData renderingData)
    {
        if (m_UseForwardPlus)
        {
            if (!m_CullingHandle.IsCompleted)
            {
                throw new InvalidOperationException("Forward+ jobs have not completed yet.");
            }

            if (m_TileMasks.Length != TinyRenderPipeline.maxTileWords)
            {
                m_ZBins.Dispose();
                m_ZBinsBuffer.Dispose();
                m_TileMasks.Dispose();
                m_TileMasksBuffer.Dispose();
                CreateForwardPlusBuffers();
            }
            else
            {
                unsafe
                {
                    UnsafeUtility.MemClear(m_ZBins.GetUnsafePtr(), m_ZBins.Length * sizeof(uint));
                    UnsafeUtility.MemClear(m_TileMasks.GetUnsafePtr(), m_TileMasks.Length * sizeof(uint));
                }
            }

            var camera = renderingData.camera;

            var screenResolution = math.int2(camera.pixelWidth, camera.pixelHeight);

            m_LightCount = renderingData.cullResults.visibleLights.Length;
            var lightOffset = 0;
            while (lightOffset < m_LightCount && renderingData.cullResults.visibleLights[lightOffset].lightType == LightType.Directional)
            {
                lightOffset++;
            }
            m_LightCount -= lightOffset;

            m_DirectionalLightCount = lightOffset;

            if (renderingData.mainLightIndex != -1 && m_DirectionalLightCount != 0) m_DirectionalLightCount -= 1;

            var visibleLights = renderingData.cullResults.visibleLights.GetSubArray(lightOffset, m_LightCount);
            var itemsPerTile = visibleLights.Length;
            m_WordsPerTile = (itemsPerTile + 31) / 32;

            m_ActualTileWidth = 8 >> 1;
            do
            {
                m_ActualTileWidth <<= 1;
                m_TileResolution = (screenResolution + m_ActualTileWidth - 1) / m_ActualTileWidth;
            } while ((m_TileResolution.x * m_TileResolution.y * m_WordsPerTile) > TinyRenderPipeline.maxTileWords);

            if (!camera.orthographic)
            {
                // Use to calculate binIndex = log2(z) * zBinScale + zBinOffset
                m_ZBinScale = (TinyRenderPipeline.maxZBinWords) / ((math.log2(camera.farClipPlane) - math.log2(camera.nearClipPlane)) * (2 + m_WordsPerTile));
                m_ZBinOffset = -math.log2(camera.nearClipPlane) * m_ZBinScale;
                m_BinCount = (int)(math.log2(camera.farClipPlane) * m_ZBinScale + m_ZBinOffset);
            }
            else
            {
                // Use to calculate binIndex = z * zBinScale + zBinOffset
                m_ZBinScale = (TinyRenderPipeline.maxZBinWords) / ((camera.farClipPlane - camera.nearClipPlane) * (2 + m_WordsPerTile));
                m_ZBinOffset = -camera.nearClipPlane * m_ZBinScale;
                m_BinCount = (int)(camera.farClipPlane * m_ZBinScale + m_ZBinOffset);
            }

            var worldToView = camera.worldToCameraMatrix;
            var viewToClip = camera.projectionMatrix;

            var minMaxZs = new NativeArray<float2>(itemsPerTile, Allocator.TempJob);
            var lightMinMaxZJob = new LightMinMaxZJob
            {
                worldToView =  worldToView,
                lights = visibleLights,
                minMaxZs = minMaxZs
            };
            // Innerloop batch count of 32 is not special, just a handwavy amount to not have too much scheduling overhead nor too little parallelism.
            var lightMinMaxZHandle = lightMinMaxZJob.ScheduleParallel(m_LightCount, 32, new JobHandle());

            var zBinningBatchCount = (m_BinCount + ZBinningJob.batchSize - 1) / ZBinningJob.batchSize;
            var zBinningJob = new ZBinningJob
            {
                bins = m_ZBins,
                minMaxZs = minMaxZs,
                zBinScale = m_ZBinScale,
                zBinOffset = m_ZBinOffset,
                binCount = m_BinCount,
                wordsPerTile = m_WordsPerTile,
                lightCount = m_LightCount,
                batchCount = zBinningBatchCount,
                isOrthographic = camera.orthographic
            };
            var zBinningHandle = zBinningJob.ScheduleParallel(zBinningBatchCount, 1, lightMinMaxZHandle);

            lightMinMaxZHandle.Complete();

            GetViewParams(camera, viewToClip, out float viewPlaneBottom0, out float viewPlaneTop0, out float4 viewToViewportScaleBias0);

            // Each light needs 1 range for Y, and a range per row. Align to 128-bytes to avoid false sharing.
            var rangesPerItem = AlignByteCount((1 + m_TileResolution.y) * UnsafeUtility.SizeOf<InclusiveRange>(), 128) / UnsafeUtility.SizeOf<InclusiveRange>();
            var tileRanges = new NativeArray<InclusiveRange>(rangesPerItem * itemsPerTile, Allocator.TempJob);
            var tilingJob = new TilingJob
            {
                lights = visibleLights,
                tileRanges = tileRanges,
                itemsPerTile = itemsPerTile,
                rangesPerItem = rangesPerItem,
                worldToView = worldToView,
                tileScale = (float2)screenResolution / m_ActualTileWidth,
                tileScaleInv = m_ActualTileWidth / (float2)screenResolution,
                viewPlaneBottom = viewPlaneBottom0,
                viewPlaneTop = viewPlaneTop0,
                viewToViewportScaleBias = viewToViewportScaleBias0,
                tileCount = m_TileResolution,
                near = camera.nearClipPlane,
                isOrthographic = camera.orthographic
            };
            var tileRangeHandle = tilingJob.ScheduleParallel(itemsPerTile, 1, lightMinMaxZHandle);

            var expansionJob = new TileRangeExpansionJob
            {
                tileRanges = tileRanges,
                tileMasks = m_TileMasks,
                rangesPerItem = rangesPerItem,
                itemsPerTile = itemsPerTile,
                wordsPerTile = m_WordsPerTile,
                tileResolution = m_TileResolution
            };
            var tilingHandle = expansionJob.ScheduleParallel(m_TileResolution.y, 1, tileRangeHandle);

            m_CullingHandle = JobHandle.CombineDependencies(
                minMaxZs.Dispose(zBinningHandle),
                tileRanges.Dispose(tilingHandle)
            );

            JobHandle.ScheduleBatchedJobs();
        }
    }

    private static int AlignByteCount(int count, int align) => align * ((count + align - 1) / align);

    // Calculate view planes and viewToViewportScaleBias. This handles projection center in case the projection is off-centered
    private void GetViewParams(Camera camera, float4x4 viewToClip, out float viewPlaneBot, out float viewPlaneTop, out float4 viewToViewportScaleBias)
    {
        // We want to calculate `fovHalfHeight = tan(fov / 2)`
        // `projection[1][1]` contains `1 / tan(fov / 2)`
        var viewPlaneHalfSizeInv = math.float2(viewToClip[0][0], viewToClip[1][1]);
        var viewPlaneHalfSize = math.rcp(viewPlaneHalfSizeInv);
        var centerClipSpace = camera.orthographic ? -math.float2(viewToClip[3][0], viewToClip[3][1]): math.float2(viewToClip[2][0], viewToClip[2][1]);

        viewPlaneBot = centerClipSpace.y * viewPlaneHalfSize.y - viewPlaneHalfSize.y;
        viewPlaneTop = centerClipSpace.y * viewPlaneHalfSize.y + viewPlaneHalfSize.y;
        viewToViewportScaleBias = math.float4(
            viewPlaneHalfSizeInv * 0.5f,
            -centerClipSpace * 0.5f + 0.5f
        );
    }

    private int m_WordsPerTile;
    private int m_ActualTileWidth;
    private int2 m_TileResolution;
    private float m_ZBinScale;
    private float m_ZBinOffset;
    private int m_BinCount;

    public void SetupLights(CommandBuffer cmd, ref RenderingData renderingData)
    {
        using (new ProfilingScope(s_SetupLightsSampler))
        {
            if (m_UseForwardPlus)
            {
                m_CullingHandle.Complete();

                using (new ProfilingScope(m_ProfilingSamplerFPUpload))
                {
                    m_ZBinsBuffer.SetData(m_ZBins.Reinterpret<float4>(UnsafeUtility.SizeOf<uint>()));
                    m_TileMasksBuffer.SetData(m_TileMasks.Reinterpret<float4>(UnsafeUtility.SizeOf<uint>()));
                    cmd.SetGlobalConstantBuffer(m_ZBinsBuffer, "_ZBinBuffer", 0, TinyRenderPipeline.maxZBinWords * 4);
                    cmd.SetGlobalConstantBuffer(m_TileMasksBuffer, "_TileBuffer", 0, TinyRenderPipeline.maxTileWords * 4);
                }

                cmd.SetGlobalVector("_FPParams0", math.float4(m_ZBinScale, m_ZBinOffset, m_LightCount, m_DirectionalLightCount));
                cmd.SetGlobalVector("_FPParams1", math.float4(renderingData.camera.pixelRect.size / m_ActualTileWidth, m_TileResolution.x, m_WordsPerTile));
                cmd.SetGlobalVector("_FPParams2", math.float4(m_BinCount, m_TileResolution.x * m_TileResolution.y, 0, 0));
            }

            SetupShaderLightConstants(cmd, ref renderingData);

            CoreUtils.SetKeyword(cmd, "_FORWARD_PLUS", m_UseForwardPlus);
        }
    }

    public void SetupRenderGraphLights(RenderGraph renderGraph, ref RenderingData renderingData)
    {
        using (var builder = renderGraph.AddLowLevelPass<SetupLightsPassData>(s_SetupLightsSampler.name, out var passData, s_SetupLightsSampler))
        {
            passData.renderingData = renderingData;
            passData.forwardLights = this;

            builder.AllowPassCulling(false);

            builder.SetRenderFunc((SetupLightsPassData data, LowLevelGraphContext lowLevelGraphContext) =>
            {
                data.forwardLights.SetupShaderLightConstants(lowLevelGraphContext.legacyCmd, ref data.renderingData);
            });
        }
    }

    public void Cleanup()
    {
        if (m_UseForwardPlus)
        {
            m_CullingHandle.Complete();
        }
    }

    private void SetupShaderLightConstants(CommandBuffer cmd, ref RenderingData renderingData)
    {
        // Main light data
        SetupMainLightConstants(cmd, ref renderingData);
        // Additional lights data
        SetupAdditionalLightConstants(cmd, ref renderingData);
    }

    private void SetupMainLightConstants(CommandBuffer cmd, ref RenderingData renderingData)
    {
        Vector4 lightPos, lightColor, lightAttenuation, lightSpotDir;
        uint lightLayerMask;
        InitializeLightConstants(renderingData.cullResults.visibleLights, renderingData.mainLightIndex, out lightPos, out lightColor, out lightAttenuation, out lightSpotDir, out lightLayerMask);

        cmd.SetGlobalVector(LightConstantBuffer._MainLightPosition, lightPos);
        cmd.SetGlobalVector(LightConstantBuffer._MainLightColor, lightColor);
        cmd.SetGlobalInt(LightConstantBuffer._MainLightLayerMask, (int)lightLayerMask);
    }

    private void SetupAdditionalLightConstants(CommandBuffer cmd, ref RenderingData renderingData)
    {
        int additionalLightsCount = SetupPerObjectLightIndices(ref renderingData);
        if (additionalLightsCount > 0)
        {
            var visibleLights = renderingData.cullResults.visibleLights;
            int maxAdditionalLightsCount = TinyRenderPipeline.maxVisibleAdditionalLights;
            for (int i = 0, lightIter = 0; i < visibleLights.Length && lightIter < maxAdditionalLightsCount; ++i)
            {
                if (renderingData.mainLightIndex != i)
                {
                    InitializeLightConstants(visibleLights, i, out m_AdditionalLightPositions[lightIter], out m_AdditionalLightColors[lightIter],
                        out m_AdditionalLightAttenuations[lightIter], out m_AdditionalLightSpotDirections[lightIter], out uint lightLayerMask);

                    m_AdditionalLightsLayerMasks[lightIter] = math.asfloat(lightLayerMask);
                    lightIter++;
                }
            }

            cmd.SetGlobalVectorArray(LightConstantBuffer._AdditionalLightsPosition, m_AdditionalLightPositions);
            cmd.SetGlobalVectorArray(LightConstantBuffer._AdditionalLightsColor, m_AdditionalLightColors);
            cmd.SetGlobalVectorArray(LightConstantBuffer._AdditionalLightsAttenuation, m_AdditionalLightAttenuations);
            cmd.SetGlobalVectorArray(LightConstantBuffer._AdditionalLightsSpotDir, m_AdditionalLightSpotDirections);
            cmd.SetGlobalFloatArray(LightConstantBuffer._AdditionalLightsLayerMasks, m_AdditionalLightsLayerMasks);

            cmd.SetGlobalVector(LightConstantBuffer._AdditionalLightsCount, new Vector4(TinyRenderPipeline.maxVisibleAdditionalLights, 0.0f, 0.0f, 0.0f));
        }
        else
        {
            cmd.SetGlobalVector(LightConstantBuffer._AdditionalLightsCount, Vector4.zero);
        }
    }

    private void InitializeLightConstants(NativeArray<VisibleLight> lights, int lightIndex, out Vector4 lightPos, out Vector4 lightColor, out Vector4 lightAttenuation, out Vector4 lightSpotDir, out uint lightLayerMask)
    {
        lightPos = LightDefaultValue.DefaultLightPosition;
        lightColor = LightDefaultValue.DefaultLightColor;
        lightAttenuation = LightDefaultValue.DefaultLightAttenuation;
        lightSpotDir = LightDefaultValue.DefaultLightSpotDirection;

        lightLayerMask = 0;

        if (lightIndex < 0)
            return;

        VisibleLight visibleLight = lights[lightIndex];
        Light light = visibleLight.light;

        if (light == null)
            return;

        var lightLocalToWorld = visibleLight.localToWorldMatrix;
        var lightType = visibleLight.lightType;

        if (lightType == LightType.Directional)
        {
            Vector4 dir = -lightLocalToWorld.GetColumn(2);
            lightPos = new Vector4(dir.x, dir.y, dir.z, 0.0f);
        }
        else
        {
            Vector4 pos = lightLocalToWorld.GetColumn(3);
            lightPos = new Vector4(pos.x, pos.y, pos.z, 1.0f);

            // Calculating distance attenuation
            GetPunctualLightDistanceAttenuation(visibleLight.range, ref lightAttenuation);

            if (lightType == LightType.Spot)
            {
                // Calculating spot light's angle attenuation
                // Spot light's outer spot angle controls how wild light cone is, inner spot angle controls when the light starts attenuating.
                GetSpotAngleAttenuation(visibleLight.spotAngle, light.innerSpotAngle, ref lightAttenuation);
                GetSpotDirection(ref lightLocalToWorld, out lightSpotDir);
            }
        }

        // VisibleLight.finalColor already returns color in active color space
        lightColor = visibleLight.finalColor;
        lightLayerMask = (uint)light.renderingLayerMask;
    }

    private int SetupPerObjectLightIndices(ref RenderingData renderingData)
    {
        if (renderingData.additionalLightsCount == 0 || m_UseForwardPlus)
            return renderingData.additionalLightsCount;

        var cullResults = renderingData.cullResults;
        var perObjectLightIndexMap = cullResults.GetLightIndexMap(Allocator.Temp);
        int globalDirectionalLightsCount = 0;
        int additionalLightsCount = 0;

        int maxVisibleAdditionalLightsCount = TinyRenderPipeline.maxVisibleAdditionalLights;
        int len = cullResults.visibleLights.Length;
        for (int i = 0; i < len; ++i)
        {
            if (additionalLightsCount >= maxVisibleAdditionalLightsCount)
                break;

            if (i == renderingData.mainLightIndex)
            {
                // Disable main light
                perObjectLightIndexMap[i] = -1;
                ++globalDirectionalLightsCount;
            }
            else
            {
                // Support additional directional light, spot light, and point light
                if (cullResults.visibleLights[i].lightType == LightType.Directional ||
                    cullResults.visibleLights[i].lightType == LightType.Spot ||
                    cullResults.visibleLights[i].lightType == LightType.Point)
                {
                    perObjectLightIndexMap[i] -= globalDirectionalLightsCount;
                }
                else
                {
                    // Disable unsupported lights
                    perObjectLightIndexMap[i] = -1;
                }

                ++additionalLightsCount;
            }
        }

        // Disable all remaining lights we cannot fit into the global light buffer
        for (int i = globalDirectionalLightsCount + additionalLightsCount; i < perObjectLightIndexMap.Length; ++i)
            perObjectLightIndexMap[i] = -1;

        cullResults.SetLightIndexMap(perObjectLightIndexMap);

        perObjectLightIndexMap.Dispose();

        return additionalLightsCount;
    }

    private void CreateForwardPlusBuffers()
    {
        m_ZBins = new NativeArray<uint>(TinyRenderPipeline.maxZBinWords, Allocator.Persistent);
        m_ZBinsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant, TinyRenderPipeline.maxZBinWords / 4, UnsafeUtility.SizeOf<float4>());
        m_ZBinsBuffer.name = "Z-Bin Buffer";

        m_TileMasks = new NativeArray<uint>(TinyRenderPipeline.maxTileWords, Allocator.Persistent);
        m_TileMasksBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant, TinyRenderPipeline.maxTileWords / 4, UnsafeUtility.SizeOf<float4>());
        m_TileMasksBuffer.name = "Tile Buffer";
    }

    private static void GetPunctualLightDistanceAttenuation(float lightRange, ref Vector4 lightAttenuation)
    {
        // Light attenuation: attenuation = 1.0 / distanceToLightSqr
        // Smooth factor: smoothFactor = saturate(1.0 - (distanceSqr / lightRangeSqr)^2)^2
        // The smooth factor makes sure that the light intensity is zero at the light range limit
        // Light intensity at distance S from light's original position:
        // lightIntensity = attenuation * smoothFactor = (1.0 / (S * S)) * (saturate(1.0 - ((S * S) / lightRangeSqr)^2)^2)

        // Store 1.0 / lightRangeSqr at lightAttenuation.x
        float lightRangeSqr = lightRange * lightRange;
        float oneOverLightRangeSqr = 1.0f / Mathf.Max(0.0001f, lightRangeSqr);
        lightAttenuation.x = oneOverLightRangeSqr;
    }

    private static void GetSpotAngleAttenuation(float outerSpotAngle, float innerSpotAngle, ref Vector4 lightAttenuation)
    {
        float cosOuterAngle = Mathf.Cos(Mathf.Deg2Rad * outerSpotAngle * 0.5f);
        float cosInnerAngle = Mathf.Cos(Mathf.Deg2Rad * innerSpotAngle * 0.5f);

        float smoothAngleRange = Mathf.Max(0.001f, cosInnerAngle - cosOuterAngle);
        float invAngleRange = 1.0f / smoothAngleRange;
        float add = -cosOuterAngle * invAngleRange;

        lightAttenuation.z = invAngleRange;
        lightAttenuation.w = add;
    }

    private static void GetSpotDirection(ref Matrix4x4 lightLocalToWorldMatrix, out Vector4 lightSpotDir)
    {
        Vector4 dir = lightLocalToWorldMatrix.GetColumn(2);
        lightSpotDir = new Vector4(-dir.x, -dir.y, -dir.z, 0.0f);
    }
}
