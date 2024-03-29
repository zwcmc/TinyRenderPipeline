using UnityEngine;
using UnityEngine.Rendering;

public partial class TinyRenderer
{
    private void DrawSkybox(ScriptableRenderContext context, Camera camera)
    {
        context.DrawSkybox(camera);
    }
}
