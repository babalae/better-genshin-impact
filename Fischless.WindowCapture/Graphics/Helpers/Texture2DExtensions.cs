using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System.Drawing.Imaging;
using Windows.Graphics.Capture;

namespace Fischless.WindowCapture.Graphics;

public static class Texture2DExtensions
{
    public static Bitmap? ToBitmap(this Direct3D11CaptureFrame frame)
    {
        var texture2dBitmap = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface);

        var d3dDevice = texture2dBitmap.Device;

        // Create texture copy
        var staging = new Texture2D(d3dDevice, new Texture2DDescription
        {
            Width = frame.ContentSize.Width,
            Height = frame.ContentSize.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = texture2dBitmap.Description.Format,
            Usage = ResourceUsage.Staging,
            SampleDescription = new SampleDescription(1, 0),
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            OptionFlags = ResourceOptionFlags.None
        });

        try
        {
            // Copy data
            d3dDevice.ImmediateContext.CopyResource(texture2dBitmap, staging);

            var dataBox = d3dDevice.ImmediateContext.MapSubresource(staging, 0, 0, MapMode.Read,
                SharpDX.Direct3D11.MapFlags.None,
                out DataStream stream);

            var bitmap = new Bitmap(staging.Description.Width, staging.Description.Height, dataBox.RowPitch,
                PixelFormat.Format32bppArgb, dataBox.DataPointer);

            return bitmap;
        }
        finally
        {
            staging.Dispose();
        }
    }
}
