using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public class TinyBaseRenderer : IDisposable
{
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) { }

    public void SetPerCameraShaderVariables(RasterCommandBuffer cmd, Camera camera, bool isTargetFlipped)
    {
        float near = camera.nearClipPlane;
        float far = camera.farClipPlane;
        float invNear = Mathf.Approximately(near, 0.0f) ? 0.0f : 1.0f / near;
        float invFar = Mathf.Approximately(far, 0.0f) ? 0.0f : 1.0f / far;
        float isOrthographic = camera.orthographic ? 1.0f : 0.0f;

        // From http://www.humus.name/temp/Linearize%20depth.txt
        // But as depth component textures on OpenGL always return in 0..1 range (as in D3D), we have to use
        // the same constants for both D3D and OpenGL here.
        // OpenGL would be this:
        // zc0 = (1.0 - far / near) / 2.0;
        // zc1 = (1.0 + far / near) / 2.0;
        // D3D is this:
        float zc0 = 1.0f - far * invNear;
        float zc1 = far * invNear;

        Vector4 zBufferParams = new Vector4(zc0, zc1, zc0 * invFar, zc1 * invFar);
        if (SystemInfo.usesReversedZBuffer)
        {
            zBufferParams.y += zBufferParams.x;
            zBufferParams.x = -zBufferParams.x;
            zBufferParams.w += zBufferParams.z;
            zBufferParams.z = -zBufferParams.z;
        }

        cmd.SetGlobalVector(ShaderPropertyId.worldSpaceCameraPos, camera.transform.position);
        cmd.SetGlobalVector(ShaderPropertyId.zBufferParams, zBufferParams);
        float aspectRatio = (float)camera.pixelWidth / (float)camera.pixelHeight;
        float orthographicSize = camera.orthographicSize;
        Vector4 orthoParams = new Vector4(orthographicSize * aspectRatio, orthographicSize, 0.0f, isOrthographic);
        cmd.SetGlobalVector(ShaderPropertyId.orthoParams, orthoParams);
        float projectionFlipSign = isTargetFlipped ? -1.0f : 1.0f;
        cmd.SetGlobalVector(ShaderPropertyId.projectionParams, new Vector4(projectionFlipSign, near, far, 1.0f * invFar));
    }
}
