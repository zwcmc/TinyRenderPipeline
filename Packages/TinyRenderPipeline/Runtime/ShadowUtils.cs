using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

public static class ShadowUtils
{
    public static RTHandle AllocShadowRT(int width, int height, int bits, string name)
    {
        var rtd = GetTemporaryShadowTextureDescriptor(width, height, bits);
        return RTHandles.Alloc(rtd, FilterMode.Bilinear, TextureWrapMode.Clamp, isShadowMap: true, name: name);
    }

    public static void SetupShadowCasterConstantBuffer(CommandBuffer cmd, VisibleLight shadowLight, Vector4 shadowBias)
    {
        cmd.SetGlobalVector("_ShadowBias", shadowBias);

        Vector3 lightDirection = -shadowLight.localToWorldMatrix.GetColumn(2);
        cmd.SetGlobalVector("_LightDirection", new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0.0f));
    }

    public static Vector4 GetShadowBias(VisibleLight shadowLight, int shadowLightIndex, Matrix4x4 lightProjectionMatrix, float shadowResolution)
    {
        if (shadowLightIndex < 0)
        {
            Debug.LogWarning(string.Format("{0} is not a valid light index.", shadowLightIndex));
            return Vector4.zero;
        }

        float frustumSize;
        if (shadowLight.lightType == LightType.Directional)
        {
            // Frustum size is guaranteed to be a cube as we wrap shadow frustum around a sphere
            frustumSize = 2.0f / lightProjectionMatrix.m00;
        }
        else
        {
            Debug.LogWarning("Only directional shadow casters are supported now.");
            frustumSize = 0.0f;
        }

        Light light = shadowLight.light;

        // depth and normal bias scale is in shadowmap texel size in world space
        float texelSize = frustumSize / shadowResolution;
        float depthBias = -light.shadowBias * texelSize;
        float normalBias = -light.shadowNormalBias * texelSize;

        return new Vector4(depthBias, normalBias, 0.0f, 0.0f);
    }

    public static void GetScaleAndBiasForLinearDistanceFade(float maxShadowDistanceSq, float border, out float scale, out float bias)
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

    // Calculates the maximum tile resolution in an Atlas.
    public static int GetMaxTileResolutionInAtlas(int atlasWidth, int atlasHeight, int tileCount)
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

    public static void ExtractDirectionalLightMatrix(ref CullingResults cullResults, int shadowLightIndex, int cascadeIndex, int cascadeCount, Vector3 cascadesSplit,
        int shadowmapWidth, int shadowmapHeight, int shadowResolution, float shadowNearPlane, out ShadowCascadeData shadowCascadeData)
    {
        cullResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(shadowLightIndex, cascadeIndex, cascadeCount,
            cascadesSplit, shadowResolution, shadowNearPlane, out shadowCascadeData.viewMatrix, out shadowCascadeData.projectionMatrix, out shadowCascadeData.splitData);

        shadowCascadeData.resolution = shadowResolution;
        shadowCascadeData.offsetX = (cascadeIndex % 2) * shadowResolution;
        shadowCascadeData.offsetY = (cascadeIndex / 2) * shadowResolution;
        shadowCascadeData.shadowTransform = GetShadowTransform(shadowCascadeData.projectionMatrix, shadowCascadeData.viewMatrix);

        // It is the culling sphere radius multiplier for shadow cascade blending
        // If this is less than 1.0, then it will begin to cull castors across cascades
        shadowCascadeData.splitData.shadowCascadeBlendCullingFactor = 1.0f;

        if (cascadeCount > 1)
            ApplySliceTransform(ref shadowCascadeData, shadowmapWidth, shadowmapHeight);
    }

    public static void ShadowRTReAllocateIfNeeded(ref RTHandle handle, int width, int height, int bits, string name = "")
    {
        if (ShadowRTNeedsReAlloc(handle, width, height, bits, name))
        {
            handle?.Release();
            handle = AllocShadowRT(width, height, bits, name);
        }
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
}
