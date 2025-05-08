using SharpDX.Direct3D11;
using System.Diagnostics;
using OpenCvSharp;

namespace Fischless.GameCapture.Graphics.Helpers;

public static class Texture2DExtensions
{
    private static Mat ConvertHdrToSdr(Mat hdrMat)
    {
        // 创建一个目标 8UC4 Mat
        var sdkMat = new Mat(hdrMat.Size(), MatType.CV_8UC4);

        // 将 32FC4 缩放到 0-255 范围并转换为 8UC4
        // 注意：这种简单缩放可能不会保留 HDR 的所有细节
        hdrMat.ConvertTo(sdkMat, MatType.CV_8UC4, 255.0);

        // 将 HDR 的 RGB 通道转换为 BGR
        Cv2.CvtColor(sdkMat, sdkMat, ColorConversionCodes.RGBA2BGRA);

        return sdkMat;
    }

    public static Mat? CreateMat(this Texture2D staging, Device d3dDevice, Texture2D surfaceTexture, ResourceRegion? region = null, bool hdr = false)
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
                SharpDX.Direct3D11.MapFlags.None);

            try
            {
                var mat = Mat.FromPixelData(staging.Description.Height, staging.Description.Width,
                    hdr? MatType.MakeType(7, 4) : MatType.CV_8UC4,
                    dataBox.DataPointer, dataBox.RowPitch);
                return hdr ? ConvertHdrToSdr(mat) : mat.Clone();
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
}
