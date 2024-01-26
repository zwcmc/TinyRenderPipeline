using UnityEngine;
using UnityEngine.Rendering;

public partial class TinyRenderer
{
    private void DrawSkybox(ScriptableRenderContext context, CommandBuffer cmd, Camera camera)
    {
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        context.DrawSkybox(camera);
    }
}
