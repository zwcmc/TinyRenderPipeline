using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class ForwardLights
{
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

    private static readonly ProfilingSampler s_SetupLightsSampler = new ProfilingSampler("SetupForwardLights");

    private class SetupLightsPassData
    {
        public RenderingData renderingData;
        public ForwardLights forwardLights;
    }

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

    public void SetupLights(CommandBuffer cmd, ref RenderingData renderingData)
    {
        using (new ProfilingScope(s_SetupLightsSampler))
        {
            SetupShaderLightConstants(cmd, ref renderingData);
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

                    m_AdditionalLightsLayerMasks[lightIter] = AsFloat((int)lightLayerMask);
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

    [StructLayout(LayoutKind.Explicit)]
    private struct IntFloatUnion
    {
        [FieldOffset(0)]
        public int intValue;

        [FieldOffset(0)]
        public float floatValue;
    }

    private static float AsFloat(int x)
    {
        IntFloatUnion u;
        u.floatValue = 0;
        u.intValue = x;

        return u.floatValue;
    }
}
