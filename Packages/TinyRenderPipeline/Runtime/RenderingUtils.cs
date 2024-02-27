using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public static class RenderingUtils
{
    private static List<ShaderTagId> m_TinyRPShaderTagIds = new List<ShaderTagId>
    {
        new ShaderTagId("TinyRPUnlit"),
        new ShaderTagId("TinyRPLit")
    };

    public static DrawingSettings CreateDrawingSettings(ref RenderingData renderingData, SortingCriteria sortingCriteria)
    {
        Camera camera = renderingData.camera;
        SortingSettings sortingSettings = new SortingSettings(camera) { criteria = sortingCriteria };
        DrawingSettings settings = new DrawingSettings(m_TinyRPShaderTagIds[0], sortingSettings)
        {
            perObjectData = renderingData.perObjectData,
            enableDynamicBatching = false,
            enableInstancing = false
        };

        for (int i = 1; i < m_TinyRPShaderTagIds.Count; ++i)
            settings.SetShaderPassName(i, m_TinyRPShaderTagIds[i]);

        return settings;
    }

    public static TextureDesc CreateTextureDesc(RenderTextureDescriptor desc, TextureSizeMode textureSizeMode = TextureSizeMode.Explicit,
        FilterMode filterMode = FilterMode.Point, TextureWrapMode wrapMode = TextureWrapMode.Clamp, string name = "")
    {
        TextureDesc rgDesc = new TextureDesc(desc.width, desc.height);
        rgDesc.sizeMode = textureSizeMode;
        rgDesc.slices = desc.volumeDepth;
        rgDesc.depthBufferBits = (DepthBits)desc.depthBufferBits;
        rgDesc.colorFormat = desc.graphicsFormat;
        rgDesc.filterMode = filterMode;
        rgDesc.wrapMode = wrapMode;
        rgDesc.dimension = desc.dimension;
        rgDesc.enableRandomWrite = desc.enableRandomWrite;
        rgDesc.useMipMap = desc.useMipMap;
        rgDesc.autoGenerateMips = desc.autoGenerateMips;
        rgDesc.isShadowMap = desc.shadowSamplingMode != ShadowSamplingMode.None;
        rgDesc.anisoLevel = 1;
        rgDesc.mipMapBias = 0;
        rgDesc.msaaSamples = (MSAASamples)desc.msaaSamples;
        rgDesc.bindTextureMS = desc.bindMS;
        rgDesc.useDynamicScale = desc.useDynamicScale;
        rgDesc.memoryless = RenderTextureMemoryless.None;
        rgDesc.vrUsage = VRTextureUsage.None;
        rgDesc.name = name;
        return rgDesc;
    }

    public static bool RTHandleNeedsReAlloc(RTHandle handle, in TextureDesc descriptor, bool scaled)
    {
        if (handle == null || handle.rt == null)
            return true;

        if (handle.useScaling != scaled)
            return true;

        if (!scaled && (handle.rt.width != descriptor.width || handle.rt.height != descriptor.height))
            return true;

        return
            (DepthBits)handle.rt.descriptor.depthBufferBits != descriptor.depthBufferBits ||
            (handle.rt.descriptor.depthBufferBits == (int)DepthBits.None && !descriptor.isShadowMap &&
            handle.rt.descriptor.graphicsFormat != descriptor.colorFormat) ||
            handle.rt.descriptor.dimension != descriptor.dimension ||
            handle.rt.descriptor.enableRandomWrite != descriptor.enableRandomWrite ||
            handle.rt.descriptor.useMipMap != descriptor.useMipMap ||
            handle.rt.descriptor.autoGenerateMips != descriptor.autoGenerateMips ||
            (MSAASamples)handle.rt.descriptor.msaaSamples != descriptor.msaaSamples ||
            handle.rt.descriptor.bindMS != descriptor.bindTextureMS ||
            handle.rt.descriptor.useDynamicScale != descriptor.useDynamicScale ||
            handle.rt.descriptor.memoryless != descriptor.memoryless ||
            handle.rt.filterMode != descriptor.filterMode ||
            handle.rt.wrapMode != descriptor.wrapMode ||
            handle.rt.anisoLevel != descriptor.anisoLevel ||
            handle.rt.mipMapBias != descriptor.mipMapBias ||
            handle.name != descriptor.name;
    }
}
