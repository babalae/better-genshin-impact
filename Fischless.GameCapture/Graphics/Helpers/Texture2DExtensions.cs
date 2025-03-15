using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System.Diagnostics;
using System.Drawing.Imaging;
using Windows.Graphics.Capture;
using OpenCvSharp;

namespace Fischless.GameCapture.Graphics.Helpers;

public static class Texture2DExtensions
{
    public static Bitmap? ToBitmap(this Direct3D11CaptureFrame frame, ResourceRegion? region = null)
    {
        var texture2dBitmap = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface);

        var d3dDevice = texture2dBitmap.Device;

        // Create texture copy
        var staging = new Texture2D(d3dDevice, new Texture2DDescription
        {
            Width = region == null ? frame.ContentSize.Width : region.Value.Right - region.Value.Left,
            Height = region == null ? frame.ContentSize.Height : region.Value.Bottom - region.Value.Top,
            MipLevels = 1,
            ArraySize = 1,
            Format = texture2dBitmap.Description.Format,
            Usage = ResourceUsage.Staging,
            SampleDescription = new SampleDescription(1, 0),
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            OptionFlags = ResourceOptionFlags.None
        });

        return staging.CreateBitmap(d3dDevice, texture2dBitmap, region);
    }

    public static Bitmap? CreateBitmap(this Texture2D staging, SharpDX.Direct3D11.Device d3dDevice, Texture2D surfaceTexture, ResourceRegion? region = null)
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

            var dataBox = d3dDevice.ImmediateContext.MapSubresource(staging, 0, 0, MapMode.Read,
                SharpDX.Direct3D11.MapFlags.None,
                out DataStream stream);

            var bitmap = new Bitmap(staging.Description.Width, staging.Description.Height, dataBox.RowPitch,
                PixelFormat.Format32bppArgb, dataBox.DataPointer);

            return bitmap;
        }
        catch (Exception e)
        {
            Debug.WriteLine("Failed to copy texture to bitmap.");
            Debug.WriteLine(e.StackTrace);
            return null;
        }
        finally
        {
            staging.Dispose();
        }
    }
    
    public static Mat? CreateMat(this Texture2D staging, SharpDX.Direct3D11.Device d3dDevice, Texture2D surfaceTexture, ResourceRegion? region = null)
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

            var mat = new Mat(staging.Description.Height, staging.Description.Width, MatType.CV_8UC4, dataBox.DataPointer);
            return mat;
        }
        catch (Exception e)
        {
            Debug.WriteLine("Failed to copy texture to mat.");
            Debug.WriteLine(e.StackTrace);
            return null;
        }
        finally
        {
            staging.Dispose();
        }
    }
}