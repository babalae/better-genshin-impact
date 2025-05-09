using SharpDX.Direct3D11;
using System.Diagnostics;
using OpenCvSharp;

namespace Fischless.GameCapture.Graphics.Helpers;

public static class Texture2DExtensions
{
    private static Mat ConvertHdrToSdr(Mat hdrMat)
    {
        // 16FC4 -> 32FC4  half float 支持不完全，转成 float 运算
        using var flatMat = new Mat();
        hdrMat.ConvertTo(flatMat, MatType.CV_32FC4);

        // HDR -> SDR
        using var expr = flatMat * 0.25;  // 曝光 -2，works on my machine
        using var sdrMat = expr.ToMat();

        // Linear RGB -> sRGB
        var srgbMat = sdrMat.Pow(1 / 2.2);

        // 32FC4 -> 8UC4
        srgbMat.ConvertTo(srgbMat, MatType.CV_8UC4, 255.0);

        // RGBA -> BGRA
        Cv2.CvtColor(srgbMat, srgbMat, ColorConversionCodes.RGBA2BGR);

        return srgbMat;
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
                MapFlags.None);

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
