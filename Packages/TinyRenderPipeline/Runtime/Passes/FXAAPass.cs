using UnityEngine;
using UnityEngine.Rendering;

public class FXAAPass
{
    private PostProcessingData m_PostProcessingData;
    private Material m_FXAAMaterial;

    private RTHandle m_Source;

    private static readonly ProfilingSampler m_ProfilingSampler = new ("ApplyFXAA");

    private static class ShaderConstants
    {
        public static readonly int _SourceSize = Shader.PropertyToID("_SourceSize");
    }

    public void Setup(in RTHandle source, PostProcessingData postProcessingData)
    {
        m_PostProcessingData = postProcessingData;
        if (m_PostProcessingData != null)
        {
            m_FXAAMaterial = CoreUtils.CreateEngineMaterial(m_PostProcessingData.shaders.fxaaShader);
        }

        m_Source = source;
    }

    public void Render(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (m_FXAAMaterial == null)
        {
            Debug.LogError("FXAA Pass: material is null");
            return;
        }

        var cmd = renderingData.commandBuffer;
        using (new ProfilingScope(cmd, m_ProfilingSampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            SetSourceSize(cmd, m_Source);

            RenderingUtils.FinalBlit(cmd, renderingData.camera, m_Source, TinyRenderPipeline.k_CameraTarget, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                m_FXAAMaterial, 0);
        }
    }

    public void Dispose()
    {
        CoreUtils.Destroy(m_FXAAMaterial);
    }

    private static void SetSourceSize(CommandBuffer cmd, RTHandle source)
    {
        float width = source.rt.width;
        float height = source.rt.height;
        if (source.rt.useDynamicScale)
        {
            width *= ScalableBufferManager.widthScaleFactor;
            height *= ScalableBufferManager.heightScaleFactor;
        }
        cmd.SetGlobalVector(ShaderConstants._SourceSize, new Vector4(width, height, 1.0f / width, 1.0f / height));
    }
}
