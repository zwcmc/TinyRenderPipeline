using UnityEngine;
using UnityEngine.Rendering;

public class PostProcessingPass
{
    private const string k_RenderPostProcessingTag = "Render PostProcessing Effects";
    private static readonly ProfilingSampler m_ProfilingRenderPostProcessing = new ProfilingSampler(k_RenderPostProcessingTag);

    private PostProcessingSettings m_PostProcessingSettings;

    private Material m_PostProcessingMaterial;

    private enum Pass
    {
        BlitCopy
    }

    public void Setup(PostProcessingSettings postProcessingSettings, Material material = null)
    {
        m_PostProcessingSettings = postProcessingSettings;
        m_PostProcessingMaterial = material;
    }

    public void Execute(ScriptableRenderContext context, ref RenderingData renderingData, ref RTHandle cameraColorAttachmentHandle)
    {
        var cmd = renderingData.commandBuffer;
        using (new ProfilingScope(cmd, m_ProfilingRenderPostProcessing))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            RenderTargetIdentifier cameraTargetID = BuiltinRenderTextureType.CameraTarget;
            RTHandleStaticHelpers.SetRTHandleStaticWrapper(cameraTargetID);
            RenderingUtils.FinalBlit(cmd, renderingData.camera, cameraColorAttachmentHandle, RTHandleStaticHelpers.s_RTHandleWrapper,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, m_PostProcessingMaterial, (int)Pass.BlitCopy);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
    }
}
