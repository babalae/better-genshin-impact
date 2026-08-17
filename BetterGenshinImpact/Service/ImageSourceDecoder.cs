using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BetterGenshinImpact.Service;

internal static class ImageSourceDecoder
{
    public static Task<ImageSource> DecodeAsync(byte[] bytes)
    {
        return StaRunner.Instance.InvokeAsync(() =>
        {
            return LooksLikeWebp(bytes)
                ? LoadWebpFromBytes(bytes)
                : LoadBitmapImageFromBytes(bytes);
        });
    }

    private static ImageSource LoadBitmapImageFromBytes(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes, writable: false);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static ImageSource LoadWebpFromBytes(byte[] bytes)
    {
        using var img = Image.Load<Rgba32>(bytes);
        var width = img.Width;
        var height = img.Height;
        var stride = width * 4;
        var buffer = new byte[stride * height];

        img.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var rowOffset = y * stride;
                for (var x = 0; x < width; x++)
                {
                    var p = row[x];
                    var a = p.A;
                    var i = rowOffset + x * 4;
                    buffer[i + 0] = Premultiply(p.B, a);
                    buffer[i + 1] = Premultiply(p.G, a);
                    buffer[i + 2] = Premultiply(p.R, a);
                    buffer[i + 3] = a;
                }
            }
        });

        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, buffer, stride);
        bmp.Freeze();
        return bmp;
    }

    private static byte Premultiply(byte c, byte a)
    {
        return (byte)((c * a + 127) / 255);
    }

    private static bool LooksLikeWebp(byte[] bytes)
    {
        return bytes.Length >= 12
               && bytes[0] == (byte)'R'
               && bytes[1] == (byte)'I'
               && bytes[2] == (byte)'F'
               && bytes[3] == (byte)'F'
               && bytes[8] == (byte)'W'
               && bytes[9] == (byte)'E'
               && bytes[10] == (byte)'B'
               && bytes[11] == (byte)'P';
    }
}

internal sealed class StaRunner
{
    public static StaRunner Instance { get; } = new();

    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    private StaRunner()
    {
        _thread = new Thread(Run) { IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void Run()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            action();
        }
    }

    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }
}
