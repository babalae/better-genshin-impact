using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Vision.WindowCapture.GraphicsCapture.Helpers;
using WinRT;

namespace Vision.WindowCapture.GraphicsCapture.Helpers
{
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

        //public static Stream ToBitmapStream(this Direct3D11CaptureFrame frame)
        //{
        //    var bitmap = frame.ToBitmap();
        //    Stream memoryStream = new MemoryStream();
        //    bitmap.Save(memoryStream, ImageFormat.Png);

        //    return memoryStream;
        //}


        //public static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(this Direct3D11CaptureFrame frame)
        //{
        //    var result = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied);

        //    return result;
        //}


        //public static async Task<Bitmap> ToBitmapAsync(this SoftwareBitmap sbitmap)
        //{
        //    using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        //    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        //    encoder.SetSoftwareBitmap(sbitmap);
        //    await encoder.FlushAsync();
        //    var bmp = new System.Drawing.Bitmap(stream.AsStream());
        //    return bmp;
        //}


        //public static Bitmap Resize(this Bitmap source, int newWidth, int newHeight)
        //{
        //    float wScale = (float)newWidth / source.Width;
        //    float hScale = (float)newHeight / source.Height;

        //    float minScale = Math.Min(wScale, hScale);

        //    var nw = (int)(source.Width * minScale);
        //    var nh = (int)(source.Height * minScale);


        //    var padDimsW = (newWidth - nw) / 2;
        //    var padDimsH = (newHeight - nh) / 2;


        //    var newBitmap = new Bitmap(newWidth, newHeight, PixelFormat.Format24bppRgb);

        //    using var g = Graphics.FromImage(newBitmap);
        //    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
        //    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        //    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
        //    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;

        //    g.DrawImage(source, new Rectangle(padDimsW, padDimsH, nw, nh),
        //        0, 0, source.Width, source.Height, GraphicsUnit.Pixel);

        //    return newBitmap;
        //}


        //public static Bitmap ResizeWithoutPadding(this Bitmap source, int new_width, int new_height)
        //{
        //    var newBitmap = new Bitmap(new_width, new_height, PixelFormat.Format24bppRgb);

        //    using var g = Graphics.FromImage(newBitmap);
        //    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
        //    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        //    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
        //    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;

        //    g.DrawImage(source, new Rectangle(0, 0, new_width, new_height),
        //        0, 0, source.Width, source.Height, GraphicsUnit.Pixel);

        //    return newBitmap;
        //}
    }
}