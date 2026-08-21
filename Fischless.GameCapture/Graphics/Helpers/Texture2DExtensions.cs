using SharpDX.Direct3D11;
using System.Diagnostics;
using OpenCvSharp;

namespace Fischless.GameCapture.Graphics.Helpers;

public static class Texture2DExtensions
{
    public static Mat? CreateMat(this Texture2D staging, Device d3dDevice, Texture2D surfaceTexture, ResourceRegion? region = null)
    {
        try
        {
            // Copy data
            if (region != null)
            {
                d3dDevice.ImmediateContext.CopySubresourceRegion(surfaceTexture, 0, region, staging, 0);
            }
            else
            {
                d3dDevice.ImmediateContext.CopyResource(surfaceTexture, staging);
            }

            // 映射纹理以便CPU读取
            var dataBox = d3dDevice.ImmediateContext.MapSubresource(
                staging,
                0,
                MapMode.Read,
                MapFlags.None);

            try
            {
                using var mat = Mat.FromPixelData(staging.Description.Height, staging.Description.Width,
                    MatType.CV_8UC4, dataBox.DataPointer, dataBox.RowPitch);
                return mat.CvtColor(ColorConversionCodes.BGRA2BGR);
            }
            finally
            {
                d3dDevice.ImmediateContext.UnmapSubresource(staging, 0);
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine("Failed to copy texture to mat.");
            Debug.WriteLine(e.StackTrace);
            return null;
        }
    }

    /// <summary>
    /// 映射（拷贝由调用方在锁内完成），使用阻塞式 MapFlags.None 与原版一致。
    /// </summary>
    public static Mat? CreateMat(this Texture2D staging, Device d3dDevice, out Mat? owner,
        Func<int, int, Mat>? acquireBgr = null, Action<Mat>? releaseBgr = null)
    {
        owner = null;
        var context = d3dDevice.ImmediateContext;
        var dataBox = context.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
        try
        {
            using Mat bgra = Mat.FromPixelData(staging.Description.Height, staging.Description.Width,
                MatType.CV_8UC4, dataBox.DataPointer, dataBox.RowPitch);
            if (acquireBgr != null)
            {
                var target = acquireBgr(staging.Description.Height, staging.Description.Width);
                try
                {
                    Cv2.CvtColor(bgra, target, ColorConversionCodes.BGRA2BGR);
                    owner = target;
                    return WgcBgrMat.CreateFrom(target, releaseBgr!);
                }
                catch
                {
                    releaseBgr?.Invoke(target);
                    throw;
                }
            }
            return bgra.CvtColor(ColorConversionCodes.BGRA2BGR);
        }
        finally
        {
            context.UnmapSubresource(staging, 0);
        }
    }

    public static Mat? CreateMat(this Texture2D staging, Device d3dDevice,
        Func<int, int, Mat>? acquireBgr = null, Action<Mat>? releaseBgr = null)
    {
        return CreateMat(staging, d3dDevice, out _, acquireBgr, releaseBgr);
    }
}
