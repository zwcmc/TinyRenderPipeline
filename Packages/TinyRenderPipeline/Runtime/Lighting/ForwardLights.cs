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
    // private static readonly ProfilingSampler m_ProfilingSamplerFPUpload = new ProfilingSampler("Forward+ Upload");

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

    private JobHandle m_CullingHandle;
    NativeArray<uint> m_ZBins;
    GraphicsBuffer m_ZBinsBuffer;
    NativeArray<uint> m_TileMasks;
    GraphicsBuffer m_TileMasksBuffer;

    private int m_LightCount;
    private int m_DirectionalLightCount;

    public ForwardLights()
    {
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
    }

    // public void PreSetup(ref RenderingData renderingData)
    // {
    //     if (m_UseForwardPlus)
    //     {
    //         if (!m_CullingHandle.IsCompleted)
    //         {
    //             throw new InvalidOperationException("Forward+ jobs have not completed yet.");
    //         }
    //
    //         if (m_TileMasks.Length != TinyRenderPipeline.maxTileWords)
    //         {
    //             m_ZBins.Dispose();
    //             m_ZBinsBuffer.Dispose();
    //             m_TileMasks.Dispose();
    //             m_TileMasksBuffer.Dispose();
    //             CreateForwardPlusBuffers();
    //         }
    //         else
    //         {
    //             unsafe
    //             {
    //                 UnsafeUtility.MemClear(m_ZBins.GetUnsafePtr(), m_ZBins.Length * sizeof(uint));
    //                 UnsafeUtility.MemClear(m_TileMasks.GetUnsafePtr(), m_TileMasks.Length * sizeof(uint));
    //             }
    //         }
    //
    //         var camera = renderingData.camera;
    //
    //         var screenResolution = math.int2(camera.pixelWidth, camera.pixelHeight);
    //
    //         // 遍历所有的可见光源，过滤出主光源和额外方向光源，因为方向光源是对场景内所有物体生效的，所以不需要做 Tiled 计算，
    //         // 所以后续所说的额外光源指的就是额外的点光源和聚光灯光源
    //         m_LightCount = renderingData.cullResults.visibleLights.Length;
    //         var lightOffset = 0;
    //         while (lightOffset < m_LightCount && renderingData.cullResults.visibleLights[lightOffset].lightType == LightType.Directional)
    //         {
    //             lightOffset++;
    //         }
    //         m_LightCount -= lightOffset;
    //         m_DirectionalLightCount = lightOffset;
    //
    //         if (renderingData.mainLightIndex != -1 && m_DirectionalLightCount != 0) m_DirectionalLightCount -= 1;
    //
    //         // 到此，m_LightCount 存储的是额外光源的总数量，m_DirectionalLightCount 存储的是额外方向光源的数量
    //
    //         // 所有额外光源
    //         var visibleLights = renderingData.cullResults.visibleLights.GetSubArray(lightOffset, m_LightCount);
    //
    //         // 额外光源的数量
    //         var itemsPerTile = visibleLights.Length;
    //
    //         // 世界空间到观察空间的转换矩阵
    //         var worldToView = camera.worldToCameraMatrix;
    //
    //         // 此 Job 用以多线程计算每个额外光源在观察空间下的最小和最大深度
    //         var minMaxZs = new NativeArray<float2>(itemsPerTile, Allocator.TempJob);
    //         var lightMinMaxZJob = new LightMinMaxZJob
    //         {
    //             worldToView =  worldToView,
    //             lights = visibleLights,
    //             minMaxZs = minMaxZs
    //         };
    //         // Innerloop batch count of 32 is not special, just a handwavy amount to not have too much scheduling overhead nor too little parallelism.
    //         var lightMinMaxZHandle = lightMinMaxZJob.ScheduleParallel(m_LightCount, 32, new JobHandle());
    //
    //         // 这段代码计算了在给定的额外光源数量下，需要多少个 "Word"（一个 "Word" 有 32 项） 来容纳这些光源。它通过加上 31 并进行整数除法来实现向上取整的效果，从而确保即使有 1 个光源也会被正确计算；
    //         // 其中，一个 "Word" 有 32 项，代表的是一个 32 位的无符号整数（uint），也就是说这里计算的是在一共有 itemsPerTile 个额外光源的情况下，需要多个 32 位的无符号整数（uint）“Word” 来存储，
    //         // 每个 “Word” 的 32 位分别可以存储一个额外光源的索引
    //         m_WordsPerTile = (itemsPerTile + 31) / 32;
    //
    //         // 在上面根据额外光源数量，计算了每个 ZBin 一共需要 m_WordsPerTile 个无符号整数，而每一个 ZBin 还包含 1 个无符号整数的 header，每个 ZBin 的结构如下：
    //         //                         ZBin 0                             ZBin 1
    //         // .-------------------------^---------------. .----------------^-------
    //         // | header | word 1 | word 2 | ... | word N | header | word 1 | ...
    //         //          `----------------v--------------'
    //         //                     m_WordsPerTile
    //         //
    //
    //         // 而申请的 m_ZBins 的长度为 TinyRenderPipeline.maxZBinWords ，也就是 TinyRenderPipeline.maxZBinWords 个无符号整数（uint）
    //         // 一共有 TinyRenderPipeline.maxZBinWords 个无符号整数（uint），而每个 ZBin 需要 (1 + m_WordsPerTile) 个无符号整数（uint），所以一共有 TinyRenderPipeline.maxZBinWords / (1 + m_WordsPerTile) 个 ZBin
    //         // 下面的逻辑是根据相机的 nearZ 和 farZ 将所有的 ZBin 均匀的分配在 nearZ -> farZ，根据距离相机原点的距离 z 就可以算出处于哪个 ZBin，zBinIndex = z * zBinScale + zBinOffset;
    //         // 如果是透视相机，则使用 log2(z) 来计算。
    //         if (!camera.orthographic)
    //         {
    //             // Use to calculate binIndex = log2(z) * zBinScale + zBinOffset
    //             m_ZBinScale = (TinyRenderPipeline.maxZBinWords) / ((math.log2(camera.farClipPlane) - math.log2(camera.nearClipPlane)) * (1 + m_WordsPerTile));
    //             m_ZBinOffset = -math.log2(camera.nearClipPlane) * m_ZBinScale;
    //             m_BinCount = (int)(math.log2(camera.farClipPlane) * m_ZBinScale + m_ZBinOffset);
    //         }
    //         else
    //         {
    //             // Use to calculate binIndex = z * zBinScale + zBinOffset
    //             m_ZBinScale = (TinyRenderPipeline.maxZBinWords) / ((camera.farClipPlane - camera.nearClipPlane) * (1 + m_WordsPerTile));
    //             m_ZBinOffset = -camera.nearClipPlane * m_ZBinScale;
    //             m_BinCount = (int)(camera.farClipPlane * m_ZBinScale + m_ZBinOffset);
    //         }
    //
    //         // 计算批处理的次数，具体来说是将 m_BinCount 个项目分成若干批次，每个批次的大小为 ZBinningJob.batchSize。为了确保所有项目都被包含进去，即使最后一个批次可能没有完全填满，这里使用了向上取整的方法。
    //         var zBinningBatchCount = (m_BinCount + ZBinningJob.batchSize - 1) / ZBinningJob.batchSize;
    //
    //         // 此 Job 用以计算每个 zBin 被哪些额外光源包含，其中 32 位无符号整数 header 的高 16 位记录的是这些额外光源的最小索引，低 16 位记录的是这些额外光源的最大索引
    //         // 每个 32 位无符号整数 “Word” 记录的是有哪些额外光源被记录（光源的索引对应 32 位中的 1 位，被记录，此位被设置为 1），例子：
    //         //    1. 当只有 lightIndex = 0 的光源被记录，则 wordIndex = 0 的 word1 的第 1 个位（从右往左数，索引从0开始）设置为 1， word1 = 00000000 00000000 00000000 00000001
    //         //    2. 而当 lightIndex = 0 和 lightIndex = 2 的光源都被记录呢？则 wordIndex = 0 的 word1 的第 3 个位（从右往左数，索引从0开始）设置为 1，此时 word1 = 00000000 00000000 00000000 00000101
    //         //    3. 如果只有 lightIndex = 33 的光源被记录，则 wordIndex = 0 的 word1 所有位都为 0，word1 = 00000000 00000000 00000000 00000000，而 wordIndex = 1 的 word2 的第 1 个位（从右往左数，索引从0开始）设置为 1，即：word2 = 00000000 00000000 00000000 00000001
    //         var zBinningJob = new ZBinningJob
    //         {
    //             bins = m_ZBins,
    //             minMaxZs = minMaxZs,
    //             zBinScale = m_ZBinScale,
    //             zBinOffset = m_ZBinOffset,
    //             binCount = m_BinCount,
    //             wordsPerTile = m_WordsPerTile,
    //             lightCount = m_LightCount,
    //             batchCount = zBinningBatchCount,
    //             isOrthographic = camera.orthographic
    //         };
    //         var zBinningHandle = zBinningJob.ScheduleParallel(zBinningBatchCount, 1, lightMinMaxZHandle);
    //
    //         lightMinMaxZHandle.Complete();
    //
    //         // 以上，在观察空间把场景从近平面到远平面把场景分成了一个个的 ZBin，每个 ZBin 包含一个 32 位无符号整数（uint）的 header，它存储的是当前 ZBin 被哪些额外光源包围的所有光源的最小索引和最大索引
    //         // 除了一个 32 位无符号整数（uint），还有 N 个 32 位无符号整数 “Word”，N 的数量取决与额外光源的数量，每个 “Word” 的 32 位用来存储当前 ZBin 被哪些额外光源包围的光源的索引。
    //
    //         // ----------------
    //         // 在深度上场景已经被分割成了一个个的 ZBin，下面要做的就是在屏幕空间也要把场景分割成一个个的 Tile
    //         // ----------------
    //
    //         // 在最大 TinyRenderPipeline.maxTileWords 个 Tile 的限制下，计算出每个 Tile 最适合的宽度
    //         // 最终每个 Tile 的尺寸为：m_ActualTileWidth x m_ActualTileWidth，屏幕在 X 轴上被分为 m_TileResolution.x 列，在 Y 轴上被分位 m_TileResolution.y 行
    //         m_ActualTileWidth = 8 >> 1;
    //         do
    //         {
    //             m_ActualTileWidth <<= 1;
    //             m_TileResolution = (screenResolution + m_ActualTileWidth - 1) / m_ActualTileWidth;
    //         } while ((m_TileResolution.x * m_TileResolution.y * m_WordsPerTile) > TinyRenderPipeline.maxTileWords);
    //
    //         // 投影矩阵
    //         var viewToClip = camera.projectionMatrix;
    //
    //         // 计算
    //         GetViewParams(camera, viewToClip, out float viewPlaneBottom, out float viewPlaneTop, out float4 viewToViewportScaleBias);
    //
    //         // Each light needs 1 range for Y, and a range per row. Align to 128-bytes to avoid false sharing.
    //         // 首先，InclusiveRange 结构体包含 2 个 short 类型的变量，分别存储的是一个范围的最小值和最大值，用来表示一个范围，一个 InclusiveRange 结构体占 2 + 2 = 4 字节数
    //         // 每个光源需在 Y 轴方向上，需要一个 InclusiveRange 结构体来存储它在 Y 轴上覆盖的 Tile 范围数据（范围值[0, m_TileResolution.y - 1]），对于在 Y 轴上的覆盖的每一行 Tiles ，都额外需要一个 InclusiveRange 结构体来存储它在 X 轴上覆盖的 Tile 范围数据（范围值[0, m_TileResolution.x - 1]）
    //         // 所以对于每一个额外光源，它一共需要 (1 + m_TileResolution.y) 个 InclusiveRange 结构体。
    //         // 总共需要的字节数是：(1 + m_TileResolution.y) * UnsafeUtility.SizeOf<InclusiveRange>()，通过 AlignByteCount 方法将总的字节数对齐到 128 字节的最小倍数，这样做看注释是为了：Align to 128-bytes to avoid false sharing. ？？？，
    //         // 最后除以每个 InclusiveRange 结构体所占的字节数计算出每个光源需要存储所有范围的 InclusiveRange 结构体数
    //         var rangesPerLight = AlignByteCount((1 + m_TileResolution.y) * UnsafeUtility.SizeOf<InclusiveRange>(), 128) / UnsafeUtility.SizeOf<InclusiveRange>();
    //
    //         // 存储所有额外光源的所有范围的数组
    //         var tileRanges = new NativeArray<InclusiveRange>(rangesPerLight * itemsPerTile, Allocator.TempJob);
    //
    //         // tileRanges 的结构如下：
    //         //                                      Light Ranges 0                                    Light Ranges 1
    //         // .----------------------------------------^----------------------------------. .----------------^-------
    //         // | yRange | xRange at yRange.min | xRange at yRange.min+1 | ... | yRange.max | yRange | word 1 | ...
    //         //          `-------------------------------v---------------------------------'
    //         //                             [yRange.min, yRange.max]
    //         //
    //
    //         // 此 JOB 用以计算每一个额外光源在屏幕上覆盖 Tile 的数据，其中有一个范围用来表示在 Y 轴上覆盖的 Tile，对于在 Y 轴上的覆盖的每一行，都额外有个范围用以记录覆盖的 X 轴范围
    //         var tilingJob = new TilingJob
    //         {
    //             lights = visibleLights,
    //             tileRanges = tileRanges,
    //             itemsPerTile = itemsPerTile,
    //             rangesPerLight = rangesPerLight,
    //             worldToView = worldToView,
    //             tileScale = (float2)screenResolution / m_ActualTileWidth,
    //             tileScaleInv = m_ActualTileWidth / (float2)screenResolution,
    //             viewPlaneBottom = viewPlaneBottom,
    //             viewPlaneTop = viewPlaneTop,
    //             viewToViewportScaleBias = viewToViewportScaleBias,
    //             tileCount = m_TileResolution,
    //             near = camera.nearClipPlane,
    //             isOrthographic = camera.orthographic
    //         };
    //         var tileRangeHandle = tilingJob.ScheduleParallel(itemsPerTile, 1, lightMinMaxZHandle);
    //
    //         var expansionJob = new TileRangeExpansionJob
    //         {
    //             tileRanges = tileRanges,
    //             tileMasks = m_TileMasks,
    //             rangesPerLight = rangesPerLight,
    //             itemsPerTile = itemsPerTile,
    //             wordsPerTile = m_WordsPerTile,
    //             tileResolution = m_TileResolution
    //         };
    //         var tilingHandle = expansionJob.ScheduleParallel(m_TileResolution.y, 1, tileRangeHandle);
    //
    //         m_CullingHandle = JobHandle.CombineDependencies(
    //             minMaxZs.Dispose(zBinningHandle),
    //             tileRanges.Dispose(tilingHandle)
    //         );
    //
    //         JobHandle.ScheduleBatchedJobs();
    //     }
    // }

    // private static int AlignByteCount(int count, int align) => align * ((count + align - 1) / align);

    // // Calculate view planes and viewToViewportScaleBias. This handles projection center in case the projection is off-centered
    // private void GetViewParams(Camera camera, float4x4 viewToClip, out float viewPlaneBot, out float viewPlaneTop, out float4 viewToViewportScaleBias)
    // {
    //     // We want to calculate `fovHalfHeight = tan(fov / 2)`
    //     // `projection[1][1]` contains `1 / tan(fov / 2)`
    //     var viewPlaneHalfSizeInv = math.float2(viewToClip[0][0], viewToClip[1][1]);
    //     var viewPlaneHalfSize = math.rcp(viewPlaneHalfSizeInv);
    //     var centerClipSpace = camera.orthographic ? -math.float2(viewToClip[3][0], viewToClip[3][1]): math.float2(viewToClip[2][0], viewToClip[2][1]);
    //
    //     viewPlaneBot = centerClipSpace.y * viewPlaneHalfSize.y - viewPlaneHalfSize.y;
    //     viewPlaneTop = centerClipSpace.y * viewPlaneHalfSize.y + viewPlaneHalfSize.y;
    //     viewToViewportScaleBias = math.float4(
    //         viewPlaneHalfSizeInv * 0.5f,
    //         -centerClipSpace * 0.5f + 0.5f
    //     );
    // }

    // private int m_WordsPerTile;
    // private int m_ActualTileWidth;
    // private int2 m_TileResolution;
    // private float m_ZBinScale;
    // private float m_ZBinOffset;
    // private int m_BinCount;

    public void SetupLights(CommandBuffer cmd, ref RenderingData renderingData)
    {
        using (new ProfilingScope(s_SetupLightsSampler))
        {
            // if (m_UseForwardPlus)
            // {
            //     m_CullingHandle.Complete();
            //
            //     using (new ProfilingScope(m_ProfilingSamplerFPUpload))
            //     {
            //         m_ZBinsBuffer.SetData(m_ZBins.Reinterpret<float4>(UnsafeUtility.SizeOf<uint>()));
            //         m_TileMasksBuffer.SetData(m_TileMasks.Reinterpret<float4>(UnsafeUtility.SizeOf<uint>()));
            //         cmd.SetGlobalConstantBuffer(m_ZBinsBuffer, "trp_ZBinBuffer", 0, TinyRenderPipeline.maxZBinWords * 4);
            //         cmd.SetGlobalConstantBuffer(m_TileMasksBuffer, "trp_TileBuffer", 0, TinyRenderPipeline.maxTileWords * 4);
            //     }
            //
            //     cmd.SetGlobalVector("_FPParams0", math.float4(m_ZBinScale, m_ZBinOffset, m_LightCount, m_DirectionalLightCount));
            //     cmd.SetGlobalVector("_FPParams1", math.float4(renderingData.camera.pixelRect.size / m_ActualTileWidth, m_TileResolution.x, m_WordsPerTile));
            // }

            SetupShaderLightConstants(cmd, ref renderingData);

            // CoreUtils.SetKeyword(cmd, "_FORWARD_PLUS", m_UseForwardPlus);
        }
    }

    public void SetupRenderGraphLights(RenderGraph renderGraph, ref RenderingData renderingData)
    {
        using (var builder = renderGraph.AddLowLevelPass<SetupLightsPassData>(s_SetupLightsSampler.name, out var passData, s_SetupLightsSampler))
        {
            passData.renderingData = renderingData;
            passData.forwardLights = this;

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((SetupLightsPassData data, LowLevelGraphContext lowLevelGraphContext) =>
            {
                data.forwardLights.SetupLights(lowLevelGraphContext.legacyCmd, ref data.renderingData);
            });
        }
    }

    public void Cleanup()
    {
        // if (m_UseForwardPlus)
        // {
        //     m_CullingHandle.Complete();
        //     m_ZBins.Dispose();
        //     m_TileMasks.Dispose();
        //     m_ZBinsBuffer.Dispose();
        //     m_ZBinsBuffer = null;
        //     m_TileMasksBuffer.Dispose();
        //     m_TileMasksBuffer = null;
        // }
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

        // For directional light, lightPos is a direction, and in light's local space, it's forward direction in local space is (0,0,1),
        // after is multiplied by light's localToWorld matrix:
        // localToWorldMatrix * (0,0,1,0), a direction has 0 in homogeneous coordinate;
        // it returns the column 2 of the light's localToWorld matrix, and in lighting calculation in Shader,
        // the light directional vector needs to point to light, so negative the direction here
        if (lightType == LightType.Directional)
        {
            Vector4 dir = lightLocalToWorld.GetColumn(2);
            lightPos = new Vector4(-dir.x, -dir.y, -dir.z, 0.0f);
        }
        else
        {
            // For point light and spot light, lightPos is a position in world space, it's original position in local space is (0,0,0),
            // after is multiplied by light's localToWorld matrix:
            // localToWorldMatrix * (0,0,0,1), a position has 1 in homogeneous coordinate;
            // it returns the column 3 of the light's localToWorld matrix
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
        if (renderingData.additionalLightsCount == 0)
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

    // private void CreateForwardPlusBuffers()
    // {
    //     m_ZBins = new NativeArray<uint>(TinyRenderPipeline.maxZBinWords, Allocator.Persistent);
    //     m_ZBinsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant, TinyRenderPipeline.maxZBinWords / 4, UnsafeUtility.SizeOf<float4>());
    //     m_ZBinsBuffer.name = "Z-Bin Buffer";
    //
    //     m_TileMasks = new NativeArray<uint>(TinyRenderPipeline.maxTileWords, Allocator.Persistent);
    //     m_TileMasksBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant, TinyRenderPipeline.maxTileWords / 4, UnsafeUtility.SizeOf<float4>());
    //     m_TileMasksBuffer.name = "Tile Buffer";
    // }

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
        // For spot light's direction, and in light's local space, it's forward direction in local space is (0,0,1),
        // after is multiplied by light's localToWorld matrix:
        // localToWorldMatrix * (0,0,1,0), a direction has 0 in homogeneous coordinate;
        // it returns the column 2 of the light's localToWorld matrix, and in lighting calculation in Shader,
        // the spot directional vector needs to point to light, so negative the direction here
        Vector4 dir = lightLocalToWorldMatrix.GetColumn(2);
        lightSpotDir = new Vector4(-dir.x, -dir.y, -dir.z, 0.0f);
    }
}
