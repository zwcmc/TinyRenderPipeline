using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public static class RenderingUtils
{
    private static List<ShaderTagId> m_TinyRPShaderTagIds = new List<ShaderTagId>
    {
        new ShaderTagId("TinyRPUnlit"),
        new ShaderTagId("TinyRPLit"),
        new ShaderTagId("SRPDefaultUnlit")
    };

    private static void AddStaleResourceToPoolOrRelease(TextureDesc desc, RTHandle handle)
    {
        if (!TinyRenderPipeline.s_RTHandlePool.AddResourceToPool(desc, handle, Time.frameCount))
        {
            RTHandles.Release(handle);
        }
    }

    public static DrawingSettings CreateDrawingSettings(ref RenderingData renderingData, SortingCriteria sortingCriteria)
    {
        Camera camera = renderingData.camera;
        SortingSettings sortingSettings = new SortingSettings(camera) { criteria = sortingCriteria };
        DrawingSettings settings = new DrawingSettings(m_TinyRPShaderTagIds[0], sortingSettings)
        {
            perObjectData = renderingData.perObjectData,
            // Disable dynamic batching
            enableDynamicBatching = false,
            // Disable instancing
            enableInstancing = false
        };

        for (int i = 1; i < m_TinyRPShaderTagIds.Count; ++i)
            settings.SetShaderPassName(i, m_TinyRPShaderTagIds[i]);

        return settings;
    }

    public static RenderTextureDescriptor GetCompatibleDescriptor(RenderTextureDescriptor desc, int width, int height,
        GraphicsFormat format, DepthBits depthBufferBits = DepthBits.None)
    {
        desc.depthBufferBits = (int)depthBufferBits;
        desc.msaaSamples = 1;
        desc.width = width;
        desc.height = height;
        desc.graphicsFormat = format;
        return desc;
    }

    public static RenderTextureDescriptor CreateRenderTextureDescriptor(Camera camera, GraphicsFormat format, bool isHdrEnabled, float renderScale = 1.0f)
    {
        int scaledWidth = (int)((float)camera.pixelWidth * renderScale);
        int scaledHeight = (int)((float)camera.pixelHeight * renderScale);

        RenderTextureDescriptor desc;
        if (camera.targetTexture == null)
        {
            desc = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight);
            desc.width = scaledWidth;
            desc.height = scaledHeight;
            desc.graphicsFormat = format;
            desc.depthBufferBits = 32;
            desc.msaaSamples = 1;
            desc.sRGB = (QualitySettings.activeColorSpace == ColorSpace.Linear);
        }
        else
        {
            desc = camera.targetTexture.descriptor;
            desc.msaaSamples = 1;
            desc.width = scaledWidth;
            desc.height = scaledHeight;

            if (camera.cameraType == CameraType.SceneView && !isHdrEnabled)
            {
                desc.graphicsFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);
            }
        }

        // Make sure dimension is non zero
        desc.width = Mathf.Max(1, desc.width);
        desc.height = Mathf.Max(1, desc.height);

        desc.enableRandomWrite = false;
        desc.useMipMap = false;
        desc.autoGenerateMips = false;

        return desc;
    }

    public static bool RTHandleNeedsReAlloc(RTHandle handle, in TextureDesc descriptor, bool scaled)
    {
        if (handle == null || handle.rt == null)
        {
            return true;
        }

        if (handle.useScaling != scaled)
        {
            return true;
        }

        if (!scaled && (handle.rt.width != descriptor.width || handle.rt.height != descriptor.height))
        {
            return true;
        }

        return (DepthBits)handle.rt.descriptor.depthBufferBits != descriptor.depthBufferBits ||
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

    public static bool ReAllocateIfNeeded(
        ref RTHandle handle,
        in RenderTextureDescriptor descriptor,
        FilterMode filterMode = FilterMode.Point,
        TextureWrapMode wrapMode = TextureWrapMode.Repeat,
        bool isShadowMap = false,
        int anisoLevel = 1,
        float mipMapBias = 0,
        string name = "")
    {
        TextureDesc requestRTDesc = RTHandleResourcePool.CreateTextureDesc(descriptor, TextureSizeMode.Explicit, anisoLevel, 0, filterMode, wrapMode, name);
        if (RTHandleNeedsReAlloc(handle, requestRTDesc, false))
        {
            if (handle != null && handle.rt != null)
            {
                TextureDesc currentRTDesc = RTHandleResourcePool.CreateTextureDesc(handle.rt.descriptor, TextureSizeMode.Explicit, handle.rt.anisoLevel, handle.rt.mipMapBias, handle.rt.filterMode, handle.rt.wrapMode, handle.name);
                AddStaleResourceToPoolOrRelease(currentRTDesc, handle);
            }

            if (TinyRenderPipeline.s_RTHandlePool.TryGetResource(requestRTDesc, out handle))
            {
                return true;
            }
            else
            {
                handle = RTHandles.Alloc(descriptor, filterMode, wrapMode, isShadowMap, anisoLevel, mipMapBias, name);
                return true;
            }
        }
        return false;
    }

    public static void FinalBlit(
        CommandBuffer cmd,
        Camera camera,
        RTHandle source,
        RTHandle destination,
        RenderBufferLoadAction loadAction,
        RenderBufferStoreAction storeAction,
        Material material, int passIndex)
    {
        var cameraType = camera.cameraType;
        bool isRenderToBackBufferTarget = cameraType != CameraType.SceneView;

        // We y-flip if
        // 1) we are blitting from render texture to back buffer(UV starts at bottom) and
        // 2) renderTexture starts UV at top
        bool yFlip = isRenderToBackBufferTarget && camera.targetTexture == null && SystemInfo.graphicsUVStartsAtTop;
        Vector4 scaleBias = yFlip ? new Vector4(1, -1, 0, 1) : new Vector4(1, 1, 0, 0);

        CoreUtils.SetRenderTarget(cmd, destination, loadAction, storeAction, ClearFlag.None, Color.clear);
        if (isRenderToBackBufferTarget)
            cmd.SetViewport(camera.pixelRect);

        Blitter.BlitTexture(cmd, source, scaleBias, material, passIndex);
    }

    public static RenderTargetIdentifier GetCameraTargetIdentifier(Camera camera)
    {
        RenderTargetIdentifier cameraTarget = (camera.targetTexture != null) ? new RenderTargetIdentifier(camera.targetTexture) : BuiltinRenderTextureType.CameraTarget;
        return cameraTarget;
    }

    public static bool IsHandleYFlipped(RTHandle handle, Camera camera)
    {
        if (!SystemInfo.graphicsUVStartsAtTop)
            return false;

        var cameraType = camera.cameraType;
        if (cameraType == CameraType.SceneView || cameraType == CameraType.Preview)
            return true;

        var handleID = new RenderTargetIdentifier(handle.nameID, 0, CubemapFace.Unknown, 0);
        bool isBackbuffer = handleID == BuiltinRenderTextureType.CameraTarget || handleID ==  BuiltinRenderTextureType.Depth;

        return !isBackbuffer;
    }

    public static TextureHandle CreateRenderGraphTexture(RenderGraph renderGraph, RenderTextureDescriptor desc, string name, bool clear,
        FilterMode filterMode = FilterMode.Point, TextureWrapMode wrapMode = TextureWrapMode.Clamp)
    {
        TextureDesc rgDesc = new TextureDesc(desc.width, desc.height);
        rgDesc.dimension = desc.dimension;
        rgDesc.clearBuffer = clear;
        rgDesc.bindTextureMS = desc.bindMS;
        rgDesc.colorFormat = desc.graphicsFormat;
        rgDesc.depthBufferBits = (DepthBits)desc.depthBufferBits;
        rgDesc.slices = desc.volumeDepth;
        rgDesc.msaaSamples = (MSAASamples)desc.msaaSamples;
        rgDesc.name = name;
        rgDesc.enableRandomWrite = desc.enableRandomWrite;
        rgDesc.filterMode = filterMode;
        rgDesc.wrapMode = wrapMode;
        rgDesc.isShadowMap = desc.shadowSamplingMode != ShadowSamplingMode.None && desc.depthStencilFormat != GraphicsFormat.None;
        // TODO RENDERGRAPH: depthStencilFormat handling?

        return renderGraph.CreateTexture(rgDesc);
    }

    public static void CreateRendererList(ScriptableRenderContext context, ref RenderingData renderingData, DrawingSettings ds, FilteringSettings fs, ref RendererList rl)
    {
        var parameters = new RendererListParams(renderingData.cullResults, ds, fs);
        rl = context.CreateRendererList(ref parameters);
    }

    public static void CreateRendererListWithRenderGraph(RenderGraph renderGraph, ref RenderingData renderingData, DrawingSettings ds, FilteringSettings fs, ref RendererListHandle rl)
    {
        var rendererListParameters = new RendererListParams(renderingData.cullResults, ds, fs);
        rl = renderGraph.CreateRendererList(rendererListParameters);
    }

    private class PassData
    {
        public TextureHandle texture;
        public string name;
        public int nameID;
    }

    private static readonly ProfilingSampler s_SetGlobalRenderGraphTextureSampler = new ProfilingSampler("SetGlobalRenderGraphTexture");

    public static void SetGlobalRenderGraphTextureName(RenderGraph renderGraph, string name, TextureHandle texture, string passName = "Set Global Texture")
    {
        using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData, s_SetGlobalRenderGraphTextureSampler))
        {
            passData.texture = builder.UseTexture(texture, IBaseRenderGraphBuilder.AccessFlags.Read);
            passData.name = name;

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext rasterGraphContext) =>
            {
                rasterGraphContext.cmd.SetGlobalTexture(data.name, data.texture);
            });
        }
    }

    public static void ScaleViewportAndBlit(RasterCommandBuffer cmd, RTHandle source, RTHandle target, ref RenderingData renderingData, Material material, int passIndex = 0)
    {
        var camera = renderingData.camera;

        // We y-flip if
        // 1) we are blitting from render texture to back buffer(UV starts at bottom) and
        // 2) renderTexture starts UV at top
        bool yFlip = IsHandleYFlipped(source, camera) != IsHandleYFlipped(target, camera);

        Vector4 scaleBias = yFlip ? new Vector4(1, -1, 0, 1) : new Vector4(1, 1, 0, 0);

        if (target.nameID == BuiltinRenderTextureType.CameraTarget || camera.targetTexture != null)
            cmd.SetViewport(camera.pixelRect);

        Blitter.BlitTexture(cmd, source, scaleBias, material, passIndex);
    }
}
