using UnityEngine.Rendering;

public class DrawSkyboxPass
{
    private static readonly ProfilingSampler m_ProfilingSampler = new ProfilingSampler("DrawSkyboxPass");

    public void ExecutePass(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cmd = renderingData.commandBuffer;
        using (new ProfilingScope(cmd, m_ProfilingSampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            context.DrawSkybox(renderingData.camera);
        }
    }
}
