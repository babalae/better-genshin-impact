using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR;


/// <summary>
/// : IOcrService
/// </summary>
[Obsolete]
public class MediaOcrService 
{
    private static readonly OcrEngine Engine =
        OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("zh-Hans-CN"));

    /// <summary>
    /// 图片太小的时候这个方法会报错，无法判断图片类型
    /// BitmapDecoder (0x88982F50)
    /// https://github.com/microsoft/CsWinRT/issues/682
    /// </summary>
    /// <param name="bitmap"></param>
    /// <returns></returns>
    public async Task<OcrResult?> OcrAsync(Bitmap bitmap)
    {
        try
        {
            var stream = new InMemoryRandomAccessStream();
            bitmap.Save(stream.AsStream(), ImageFormat.Png);
            var decoder = BitmapDecoder.CreateAsync(stream).GetAwaiter().GetResult();
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
            var ocrResult = await Engine.RecognizeAsync(softwareBitmap);
            softwareBitmap.Dispose();
            return ocrResult;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.Message);
            return null;
        }
    }

    public async Task<OcrResult?> OcrAsyncByBytes(Bitmap bitmap)
    {
        try
        {
            var bytes = BitmapToByte(bitmap);
            var stream = new MemoryStream(bytes);
            var decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
            var ocrResult = await Engine.RecognizeAsync(softwareBitmap);
            softwareBitmap.Dispose();
            return ocrResult;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.Message);
            return null;
        }
    }

    /// <summary>
    /// 将BitMap转换成bytes数组
    /// </summary>
    /// <param name="bitmap">要转换的图像</param>
    /// <returns></returns>
    private byte[] BitmapToByte(Bitmap bitmap)
    {
        // 1.先将BitMap转成内存流
        var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Bmp);
        ms.Seek(0, SeekOrigin.Begin);
        // 2.再将内存流转成byte[]并返回
        byte[] bytes = new byte[ms.Length];
        var _ = ms.Read(bytes, 0, bytes.Length);
        ms.Dispose();
        return bytes;
    }

    //public async Task<OcrResult?> OcrAsyncByFile(Bitmap bitmap)
    //{
    //    try
    //    {
    //        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp.png");
    //        bitmap.Save(path);
    //        var storageFile = await StorageFile.GetFileFromPathAsync(path);
    //        IRandomAccessStream randomAccessStream = await storageFile.OpenReadAsync();
    //        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
    //        var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
    //        var ocrResult = await Engine.RecognizeAsync(softwareBitmap);
    //        softwareBitmap.Dispose();
    //        return ocrResult;
    //    }
    //    catch (Exception e)
    //    {
    //        Console.WriteLine(e.Message);
    //        return null;
    //    }
    //}

    public string Ocr(Bitmap bitmap)
    {
        var ocrResult = OcrAsyncByBytes(bitmap).GetAwaiter().GetResult();

        if (ocrResult == null)
        {
            return "";
        }
        else
        {
            Debug.WriteLine("MediaOcr结果: " + ocrResult.Text);
            return ocrResult.Text;
        }
    }

    public string Ocr(Mat mat)
    {
        throw new NotImplementedException();
    }
}