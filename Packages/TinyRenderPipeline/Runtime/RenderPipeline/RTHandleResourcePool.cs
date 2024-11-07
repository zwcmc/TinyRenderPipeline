using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class RTHandleResourcePool
{
    // Dictionary tracks resources by hash and stores resources with same hash in a List (list instead of a stack because we need to be able to remove stale allocations, potentially in the middle of the stack).
    // The list needs to be sorted otherwise you could get inconsistent resource usage from one frame to another.
    private Dictionary<int, SortedList<int, (RTHandle resource, int frameIndex)>> m_ResourcePool = new Dictionary<int, SortedList<int, (RTHandle resource, int frameIndex)>>();

    // Used to remove stale resources as there is no RemoveAll on SortedLists
    private List<int> m_RemoveList = new List<int>(32);

    private static int s_CurrentStaleResourceCount = 0;
    // Keep stale resources alive for 3 frames
    private static int s_StaleResourceLifetime = 3;
    // Store max 32 rtHandles
    // 1080p * 32bpp * 32 = 265.4mb
    private static int s_StaleResourceMaxCapacity = 32;

    // Add no longer used resouce to pool
    // Return true if resource is added to pool successfully, return false otherwise.
    public bool AddResourceToPool(in TextureDesc texDesc, RTHandle resource, int currentFrameIndex)
    {
        // Check staled count
        if (s_CurrentStaleResourceCount >= s_StaleResourceMaxCapacity)
            return false;

        int hashCode = GetHashCodeWithNameHash(texDesc);
        if (!m_ResourcePool.TryGetValue(hashCode, out var list))
        {
            // Init list with max capacity to avoid runtime GC.Alloc when calling list.Add(resize list)
            list = new SortedList<int, (RTHandle resource, int frameIndex)>(s_StaleResourceMaxCapacity);
            m_ResourcePool.Add(hashCode, list);
        }

        list.Add(resource.GetInstanceID(), (resource, currentFrameIndex));
        s_CurrentStaleResourceCount++;

        return true;
    }

    // Get resource from the pool using TextureDesc as key
    // Return true if resource successfully retried resource from the pool, return false otherwise.
    public bool TryGetResource(in TextureDesc texDesc, out RTHandle resource, bool usePool = true)
    {
        int hashCode = GetHashCodeWithNameHash(texDesc);
        if (usePool && m_ResourcePool.TryGetValue(hashCode, out SortedList<int, (RTHandle resource, int frameIndex)> list) && list.Count > 0)
        {
            resource = list.Values[list.Count - 1].resource;
            list.RemoveAt(list.Count - 1);
            s_CurrentStaleResourceCount--;
            return true;
        }

        resource = null;
        return false;
    }

    // Release all resources in pool.
    public void Cleanup()
    {
        foreach (var kvp in m_ResourcePool)
        {
            foreach (var res in kvp.Value)
            {
                res.Value.resource.Release();
            }
        }
        m_ResourcePool.Clear();

        s_CurrentStaleResourceCount = 0;
    }

    // Release resources that are not used in last couple frames.
    public void PurgeUnusedResources(int currentFrameIndex)
    {
        // Update the frame index for the lambda. Static because we don't want to capture.
        m_RemoveList.Clear();

        foreach (var kvp in m_ResourcePool)
        {
            // WARNING: No foreach here. Sorted list GetEnumerator generates garbage...
            var list = kvp.Value;
            var keys = list.Keys;
            var values = list.Values;
            for (int i = 0; i < list.Count; ++i)
            {
                var value = values[i];
                if (ShouldReleaseResource(value.frameIndex, currentFrameIndex))
                {
                    value.resource.Release();
                    m_RemoveList.Add(keys[i]);
                    s_CurrentStaleResourceCount--;
                }
            }

            foreach (var key in m_RemoveList)
                list.Remove(key);
        }
    }

    private static bool ShouldReleaseResource(int lastUsedFrameIndex, int currentFrameIndex)
    {
        // We need to have a delay of a few frames before releasing resources for good.
        // Indeed, when having multiple off-screen cameras, they are rendered in a separate SRP render call and thus with a different frame index than main camera
        // This causes texture to be deallocated/reallocated every frame if the two cameras don't need the same buffers.
        return (lastUsedFrameIndex + s_StaleResourceLifetime) < currentFrameIndex;
    }

    // NOTE: Only allow reusing resource with the same name.
    // This is because some URP code uses texture name as key to bind input texture (GBUFFER_2). Different name will result in URP bind texture to different shader input slot.
    // Ideally if URP code uses shaderPropertyID(instead of name string), we can relax the restriction here.
    private int GetHashCodeWithNameHash(in TextureDesc texDesc)
    {
        int hashCode = texDesc.GetHashCode();
        hashCode = hashCode * 23 + texDesc.name.GetHashCode();

        return hashCode;
    }

    public static TextureDesc CreateTextureDesc(RenderTextureDescriptor desc,
        TextureSizeMode textureSizeMode = TextureSizeMode.Explicit, int anisoLevel = 1, float mipMapBias = 0,
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
        rgDesc.anisoLevel = anisoLevel;
        rgDesc.mipMapBias = mipMapBias;
        rgDesc.msaaSamples = (MSAASamples)desc.msaaSamples;
        rgDesc.bindTextureMS = desc.bindMS;
        rgDesc.useDynamicScale = desc.useDynamicScale;
        rgDesc.memoryless = RenderTextureMemoryless.None;
        rgDesc.vrUsage = VRTextureUsage.None;
        rgDesc.name = name;
        return rgDesc;
    }
}
