#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public partial class TinyRenderer
{
    private static class Profiling
    {
        public static readonly ProfilingSampler drawOpaque = new ProfilingSampler($"{nameof(DrawOpaque)}");
        public static readonly ProfilingSampler drawTransparent = new ProfilingSampler($"{nameof(DrawTransparent)}");
        public static readonly ProfilingSampler drawGizmos = new ProfilingSampler($"{nameof(DrawGizmos)}");
    }

    private PostProcessingSettings m_PostProcessingSettings;

    private ForwardLights m_ForwardLights;
    private MainLightShadowPass m_MainLightShadowPass;
    private AdditionalLightsShadowPass m_AdditionalLightsShadowPass;
    private PostProcessingPass m_PostProcessingPass;

    private RTHandle m_CameraColorAttachmentHandle;
    private RTHandle m_CameraDepthAttachmentHandle;

    private RTHandle m_TargetColorHandle;
    private RTHandle m_TargetDepthHandle;

    private Material m_PostProcessingMaterial;

    public TinyRenderer(PostProcessingSettings postProcessingSettings)
    {
        m_PostProcessingSettings = postProcessingSettings;

        if (m_PostProcessingSettings != null)
        {
            m_PostProcessingMaterial = CoreUtils.CreateEngineMaterial(m_PostProcessingSettings.postProcessingShader);
        }

        m_ForwardLights = new ForwardLights();
        m_MainLightShadowPass = new MainLightShadowPass();
        m_AdditionalLightsShadowPass = new AdditionalLightsShadowPass();
        m_PostProcessingPass = new PostProcessingPass();
    }

    public void Execute(ref RenderingData renderingData)
    {
        var context = renderingData.renderContext;
        var camera = renderingData.camera;
        var cmd = renderingData.commandBuffer;

        // Setup lighting data
        m_ForwardLights.Setup(context, ref renderingData);

        // Render main light shadowmap
        if (m_MainLightShadowPass.Setup(ref renderingData))
        {
            m_MainLightShadowPass.Render(context, ref renderingData);
        }

        // Render additional lights shadowmap
        if (m_AdditionalLightsShadowPass.Setup(ref renderingData))
        {
            m_AdditionalLightsShadowPass.Render(context, ref renderingData);
        }

        // Setup camera properties
        SetCameraProperties(context, camera);

        // Post processing
        bool applyPostProcessingEffects = (m_PostProcessingSettings != null) && (m_PostProcessingMaterial != null) &&
                                          (camera.cameraType <= CameraType.SceneView);

        // Check if disable post processing effects in scene view
        applyPostProcessingEffects &= CoreUtils.ArePostProcessesEnabled(camera);

        CameraClearFlags clearFlag = camera.clearFlags;
        if (applyPostProcessingEffects)
        {
            m_PostProcessingPass.Setup(m_PostProcessingSettings, m_PostProcessingMaterial);

            var cameraDescriptor = renderingData.cameraTargetDescriptor;
            cameraDescriptor.useMipMap = false;
            cameraDescriptor.autoGenerateMips = false;
            cameraDescriptor.depthBufferBits = (int)DepthBits.None;
            cameraDescriptor.bindMS = false;
            RenderingUtils.ReAllocateIfNeeded(ref m_CameraColorAttachmentHandle, cameraDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_CameraColorAttachment");

            cameraDescriptor.graphicsFormat = GraphicsFormat.None;
            cameraDescriptor.depthStencilFormat = GraphicsFormat.D32_SFloat_S8_UInt;;
            RenderingUtils.ReAllocateIfNeeded(ref m_CameraDepthAttachmentHandle, cameraDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_CameraDepthAttachment");

            // Configure target
            cmd.SetRenderTarget(m_CameraColorAttachmentHandle, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                m_CameraDepthAttachmentHandle, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);

            // When drawing to an intermediate frame buffer we render to a texture filled with arbitrary data.
            // To prevent random results, when post-processing enabled always clear depth and color.
            // ----------------
            // Dont understand why doing this?
            // ----------------
            if (clearFlag > CameraClearFlags.Color)
                clearFlag = CameraClearFlags.Color;
        }
        else
        {
            RenderTargetIdentifier targetId = BuiltinRenderTextureType.CameraTarget;
            m_TargetColorHandle ??= RTHandles.Alloc(targetId);
            m_TargetDepthHandle ??= RTHandles.Alloc(targetId);

            // Configure target
            cmd.SetRenderTarget(m_TargetColorHandle, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                m_TargetDepthHandle, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
        }

        cmd.ClearRenderTarget(clearFlag <= CameraClearFlags.Depth, clearFlag <= CameraClearFlags.Color,
            clearFlag == CameraClearFlags.Color ? camera.backgroundColor.linear : Color.clear);
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        DrawOpaque(context, ref renderingData);

        DrawSkybox(context, camera);

        DrawTransparent(context, ref renderingData);

        DrawGizmos(context, cmd, camera, GizmoSubset.PreImageEffects);

        if (applyPostProcessingEffects)
        {
            m_PostProcessingPass.Execute(context, ref renderingData, ref m_CameraColorAttachmentHandle);
        }

        DrawGizmos(context, cmd, camera, GizmoSubset.PostImageEffects);
    }

    public void Dispose(bool disposing)
    {
        m_MainLightShadowPass?.Dispose();
        m_AdditionalLightsShadowPass?.Dispose();

        m_CameraColorAttachmentHandle?.Release();
        m_CameraDepthAttachmentHandle?.Release();

        m_TargetColorHandle?.Release();
        m_TargetDepthHandle?.Release();

        CoreUtils.Destroy(m_PostProcessingMaterial);
    }

    private void SetCameraProperties(ScriptableRenderContext context, Camera camera)
    {
        context.SetupCameraProperties(camera);
    }

    private void DrawGizmos(ScriptableRenderContext context, CommandBuffer cmd, Camera camera, GizmoSubset gizmoSubset)
    {
#if UNITY_EDITOR
        if (!Handles.ShouldRenderGizmos() || camera.sceneViewFilterMode == Camera.SceneViewFilterMode.ShowFiltered)
            return;

        using (new ProfilingScope(cmd, Profiling.drawGizmos))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            context.DrawGizmos(camera, gizmoSubset);
        }

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
#endif
    }
}
