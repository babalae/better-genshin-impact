using SharpDX.Direct3D11;
using System.Diagnostics;
using OpenCvSharp;

namespace Fischless.GameCapture.Graphics.Helpers;

public static class Texture2DExtensions
{
    public static Mat? CreateMat(
        this Texture2D staging,
        DeviceContext context,
        Texture2D surfaceTexture,
        ResourceRegion? region = null)
    {
        try
        {
            // Copy data
            if (region != null)
            {
                context.CopySubresourceRegion(surfaceTexture, 0, region, staging, 0);
            }
            else
            {
                context.CopyResource(surfaceTexture, staging);
            }

            // 映射纹理以便CPU读取
            var dataBox = context.MapSubresource(
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
                context.UnmapSubresource(staging, 0);
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
