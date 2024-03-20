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

        Vector4 lightDirectionOrPosition;
        switch (shadowLight.lightType)
        {
            case LightType.Directional:
                Vector3 lightDirection = -shadowLight.localToWorldMatrix.GetColumn(2);
                lightDirectionOrPosition = new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 1.0f);
                break;
            case LightType.Spot:
            case LightType.Point:
                Vector3 lightPosition = shadowLight.localToWorldMatrix.GetColumn(3);
                lightDirectionOrPosition = new Vector4(lightPosition.x, lightPosition.y, lightPosition.z, 0.0f);
                break;
            default:
                lightDirectionOrPosition = new Vector4(0, 0, 1, 1);
                break;
        }
        cmd.SetGlobalVector("_LightDirectionOrPosition", lightDirectionOrPosition);
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
        else if (shadowLight.lightType == LightType.Spot)
        {
            // For perspective projections, shadow texel size varies with depth
            // It will only work well if done in receiver side in the pixel shader. Currently UniversalRP
            // do bias on caster side in vertex shader. When we add shader quality tiers we can properly
            // handle this. For now, as a poor approximation we do a constant bias and compute the size of
            // the frustum as if it was orthogonal considering the size at mid point between near and far planes.
            // Depending on how big the light range is, it will be good enough with some tweaks in bias
            frustumSize = Mathf.Tan(shadowLight.spotAngle * 0.5f * Mathf.Deg2Rad) * shadowLight.range; // half-width (in world-space units) of shadow frustum's "far plane"
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

    public static bool ExtractDirectionalLightMatrix(ref CullingResults cullResults, int shadowLightIndex, int cascadeIndex, int cascadeCount, Vector3 cascadesSplit,
        int shadowmapWidth, int shadowmapHeight, int shadowResolution, float shadowNearPlane, out ShadowSliceData shadowCascadeData)
    {
        bool success = cullResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(shadowLightIndex, cascadeIndex, cascadeCount,
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

        return success;
    }

    public static bool ExtractSpotLightMatrix(ref CullingResults cullResults, int shadowLightIndex, out ShadowSliceData shadowSliceData)
    {
        shadowSliceData = default;
        bool success = cullResults.ComputeSpotShadowMatricesAndCullingPrimitives(shadowLightIndex, out shadowSliceData.viewMatrix, out shadowSliceData.projectionMatrix, out shadowSliceData.splitData);
        shadowSliceData.shadowTransform = GetShadowTransform(shadowSliceData.projectionMatrix, shadowSliceData.viewMatrix);
        return success;
    }

    public static void ShadowRTReAllocateIfNeeded(ref RTHandle handle, int width, int height, int bits, string name = "")
    {
        if (ShadowRTNeedsReAlloc(handle, width, height, bits, name))
        {
            handle?.Release();
            handle = AllocShadowRT(width, height, bits, name);
        }
    }

    public static int GetAdditionalLightShadowSliceCount(in LightType lightType)
    {
        switch (lightType)
        {
            case LightType.Spot:
                return 1;
            case LightType.Point:
                return 6;
            default:
                return 0;
        }
    }

    public static bool IsValidShadowCastingLight(ref CullingResults cullResults, int mainLightIndex, int visibleLightIndex)
    {
        if (visibleLightIndex == mainLightIndex)
            return false;

        VisibleLight vl = cullResults.visibleLights[visibleLightIndex];

        if (vl.lightType == LightType.Directional)
            return false;

        Light light = vl.light;
        return light != null && light.shadows != LightShadows.None && !Mathf.Approximately(light.shadowStrength, 0.0f);
    }

    public static void ApplySliceTransform(ref ShadowSliceData shadowCascadeData, int atlasWidth, int atlasHeight)
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
}