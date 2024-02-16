using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class ForwardLights
{
    private static class LightDefaultValue
    {
        public static Vector4 DefaultLightPosition = new Vector4(0.0f, 0.0f, 1.0f, 0.0f);
        public static Vector4 DefaultLightColor = Color.black;
    }

    private static class LightConstantBuffer
    {
        public static int _MainLightPosition = Shader.PropertyToID("_MainLightPosition");
        public static int _MainLightColor = Shader.PropertyToID("_MainLightColor");
    }

    public void Setup(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cmd = renderingData.commandBuffer;

        SetupLights(cmd, ref renderingData);

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
    }

    private void SetupLights(CommandBuffer cmd, ref RenderingData renderingData)
    {
        SetupShaderLightConstants(cmd, ref renderingData);
    }

    private void SetupShaderLightConstants(CommandBuffer cmd, ref RenderingData renderingData)
    {
        SetupMainLightConstants(cmd, ref renderingData);
    }

    private void SetupMainLightConstants(CommandBuffer cmd, ref RenderingData renderingData)
    {
        Vector4 lightPos, lightColor;
        InitializeLightConstants(renderingData.cullResults.visibleLights, renderingData.mainLightIndex, out lightPos, out lightColor);

        cmd.SetGlobalVector(LightConstantBuffer._MainLightPosition, lightPos);
        cmd.SetGlobalVector(LightConstantBuffer._MainLightColor, lightColor);
    }

    private void InitializeLightConstants(NativeArray<VisibleLight> lights, int lightIndex, out Vector4 lightPos, out Vector4 lightColor)
    {
        lightPos = LightDefaultValue.DefaultLightPosition;
        lightColor = LightDefaultValue.DefaultLightColor;

        if (lightIndex < 0)
            return;

        VisibleLight visibleLight = lights[lightIndex];

        if (visibleLight.light == null)
            return;

        var lightLocalToWorld = visibleLight.localToWorldMatrix;
        var lightType = visibleLight.lightType;

        if (lightType == LightType.Directional)
        {
            Vector4 dir = -lightLocalToWorld.GetColumn(2);
            lightPos = new Vector4(dir.x, dir.y, dir.z, 0.0f);
        }

        lightColor = visibleLight.finalColor;
    }
}
