using UnityEngine;
using UnityEngine.Rendering;

public class FrameHistory
{
    private static readonly int s_HaltonSampleCount = 16;
    private static Vector2[] s_Halton23Samples = new Vector2[s_HaltonSampleCount];

    public static int LastFrameIndex;
    public static Matrix4x4 LastFrameView;
    public static Matrix4x4 LastFrameProjection;
    public static Matrix4x4 LastFrameJitteredProjection;

    public static Matrix4x4 CurrentFrameView;
    public static Matrix4x4 CurrentFrameProjection;
    public static Matrix4x4 CurrentFrameJitteredProjection;
    public static Vector2 TaaJitter;

    public static readonly string s_SsrHistoryColorTextureName = "_SsrHistoryColorTexture";
    public static RTHandle s_SsrHistoryColorRT;

    public static void Initialize()
    {
        LastFrameIndex = -1;

        LastFrameView = Matrix4x4.identity;
        LastFrameProjection = Matrix4x4.identity;
        LastFrameJitteredProjection = Matrix4x4.identity;

        CurrentFrameView = Matrix4x4.identity;
        CurrentFrameProjection = Matrix4x4.identity;
        CurrentFrameJitteredProjection = Matrix4x4.identity;

        TaaJitter = Vector2.zero;

        // Generate halton23 sequence
        for (int i = 0; i < s_HaltonSampleCount; ++i)
        {
            s_Halton23Samples[i].x = Halton(i, 2);
            s_Halton23Samples[i].y = Halton(i, 3);
        }
    }

    public static void UpdateFrameInfo(int frameIndex, ref RenderingData renderingData)
    {
        ref var cameraData = ref renderingData.cameraData;
        var taaEnabled = renderingData.antialiasing == AntialiasingMode.TemporalAntialiasing;

        Matrix4x4 viewMatrix = cameraData.camera.worldToCameraMatrix;
        Matrix4x4 projectionMatrix = cameraData.camera.projectionMatrix;

        // First frame
        if (LastFrameIndex == -1)
        {
            LastFrameView = viewMatrix;
            LastFrameProjection = projectionMatrix;
            LastFrameJitteredProjection = projectionMatrix;
        }

        LastFrameView = CurrentFrameView;
        LastFrameProjection = CurrentFrameProjection;
        LastFrameJitteredProjection = CurrentFrameJitteredProjection;

        CurrentFrameView = viewMatrix;
        CurrentFrameProjection = projectionMatrix;
        CurrentFrameJitteredProjection = projectionMatrix;

        if (taaEnabled)
        {
            TaaJitter = s_Halton23Samples[frameIndex % s_HaltonSampleCount];
            // 添加屏幕像素抖动偏移
            float cameraTargetWidth = (float)cameraData.targetDescriptor.width;
            float cameraTargetHeight = (float)cameraData.targetDescriptor.height;
            CurrentFrameJitteredProjection.m02 -= TaaJitter.x * (2.0f / cameraTargetWidth);
            CurrentFrameJitteredProjection.m12 -= TaaJitter.y * (2.0f / cameraTargetHeight);
        }

        LastFrameIndex = frameIndex;
    }

    public static Matrix4x4 GetCurrentFrameView()
    {
        return CurrentFrameView;
    }

    public static Matrix4x4 GetCurrentFrameProjection()
    {
        return CurrentFrameProjection;
    }

    public static Matrix4x4 GetCurrentFrameJitteredProjection()
    {
        return CurrentFrameJitteredProjection;
    }

    public static Matrix4x4 GetLastFrameView()
    {
        return LastFrameView;
    }

    public static Matrix4x4 GetLastFrameProjection()
    {
        return LastFrameProjection;
    }

    public static Matrix4x4 GetLastFrameJitteredProjection()
    {
        return LastFrameJitteredProjection;
    }

    public static RTHandle GetSsrHistoryColorRT()
    {
        return s_SsrHistoryColorRT;
    }

    public static void Reset()
    {
        LastFrameIndex = -1;

        LastFrameView = Matrix4x4.identity;
        LastFrameProjection = Matrix4x4.identity;
        LastFrameJitteredProjection = Matrix4x4.identity;
        CurrentFrameView = Matrix4x4.identity;
        CurrentFrameProjection = Matrix4x4.identity;
        CurrentFrameJitteredProjection = Matrix4x4.identity;

        TaaJitter = Vector2.zero;

        s_SsrHistoryColorRT?.Release();
        s_SsrHistoryColorRT = null;
    }

    private static float Halton(int i, int b)
    {
        // 跳过序列前面的元素使得生成出的序列元素的平均值更接近 0.5
        i += 409;

        float f = 1.0f;
        float r = 0.0f;
        while (i > 0)
        {
            f /= (float)b;
            r += f * (float)(i % b);
            i /= b;
        }
        return r;
    }
}
