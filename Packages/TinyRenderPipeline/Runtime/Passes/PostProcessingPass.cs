using UnityEngine;
using UnityEngine.Rendering;

public class PostProcessingPass
{
    private const string k_RenderPostProcessingTag = "Render PostProcessing Effects";
    private static readonly ProfilingSampler m_ProfilingRenderPostProcessing = new ProfilingSampler(k_RenderPostProcessingTag);

    public static RTHandle k_CameraTarget = RTHandles.Alloc(BuiltinRenderTextureType.CameraTarget);

    private PostProcessingSettings m_PostProcessingSettings;
    private Material m_PostProcessingMaterial;

    private RTHandle m_Source;
    private RTHandle m_Destination;

    private enum Pass
    {
        BlitCopy
    }

    public bool isValid { get => (m_PostProcessingSettings != null) && (m_PostProcessingMaterial != null); }

    public PostProcessingPass(TinyRenderPipelineAsset asset)
    {
        m_PostProcessingSettings = null;
        m_PostProcessingMaterial = null;

        if (asset)
        {
            m_PostProcessingSettings = asset.postProcessingSettings;
            if (m_PostProcessingSettings != null)
                m_PostProcessingMaterial = CoreUtils.CreateEngineMaterial(m_PostProcessingSettings.postProcessingShader);
        }
    }

    public void Setup(in RTHandle source)
    {
        m_Source = source;
        m_Destination = k_CameraTarget;
    }

    public void Execute(ScriptableRenderContext context, ref RenderingData renderingData, ref RTHandle cameraColorAttachmentHandle)
    {
        var cmd = renderingData.commandBuffer;
        using (new ProfilingScope(cmd, m_ProfilingRenderPostProcessing))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            Render(cmd, ref renderingData);
        }
    }

    private void Render(CommandBuffer cmd, ref RenderingData renderingData)
    {
        RTHandle source = m_Source;
        RTHandle destination = m_Destination;

        RenderingUtils.FinalBlit(cmd, renderingData.camera, source, destination, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, m_PostProcessingMaterial, (int)Pass.BlitCopy);
    }

    public void Dispose()
    {
        CoreUtils.Destroy(m_PostProcessingMaterial);
        m_PostProcessingMaterial = null;
    }
}
