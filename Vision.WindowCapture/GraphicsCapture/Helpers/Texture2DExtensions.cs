using System.Drawing;
using System.Drawing.Imaging;
using Windows.Graphics.Capture;
using Windows.Win32;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi.Common;

namespace Vision.WindowCapture.GraphicsCapture.Helpers
{
    public static class Texture2DExtensions
    {
        public static unsafe Bitmap ToBitmap(this Direct3D11CaptureFrame frame)
        {
            var d3d11Texture2d = Direct3D11Helper.CreateD3D11Texture2D(frame.Surface);

            d3d11Texture2d.GetDevice(out var d3dDevice);
            d3d11Texture2d.GetDesc(out D3D11_TEXTURE2D_DESC desc);
            D3D11_TEXTURE2D_DESC newDesc = new()
            {
                Width = (uint)frame.ContentSize.Width,
                Height = (uint)frame.ContentSize.Height,
                MipLevels = 1U,
                ArraySize = 1U,
                Format = desc.Format,
                Usage = D3D11_USAGE.D3D11_USAGE_STAGING,
                SampleDesc = new DXGI_SAMPLE_DESC() { Count = 1 },
                CPUAccessFlags = D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ,
            };

            // Create texture copy
            d3dDevice.CreateTexture2D(newDesc, default, out ID3D11Texture2D buffer);
            d3dDevice.GetImmediateContext(out ID3D11DeviceContext context);

            // Copy data
            context.CopyResource(buffer, d3d11Texture2d);
            D3D11_MAPPED_SUBRESOURCE subResource = default;
            context.Map(buffer, default, D3D11_MAP.D3D11_MAP_READ, default, &subResource);

            buffer.GetDesc(out var stagingDesc);
            var bitmap = new Bitmap(
                (int)stagingDesc.Width,
                (int)stagingDesc.Height,
                (int)subResource.RowPitch,
                PixelFormat.Format32bppArgb,
                (nint)subResource.pData);

            return bitmap;
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