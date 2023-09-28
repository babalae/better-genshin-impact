using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Shapes;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;
using Path = System.IO.Path;

namespace Vision.Recognition.Helper.OCR
{
    public class MediaOcr : IOcrService
    {
        private static readonly OcrEngine Engine =
            OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("zh-Hans-CN"));
        public static string StartUpPath { get; private set; } = AppDomain.CurrentDomain.BaseDirectory;

        public static string Absolute(string relativePath)
        {
            return Path.Combine(StartUpPath, relativePath);
        }

        /// <summary>
        /// 图片太小的时候这个方法会报错，无法判断图片类型
        /// BitmapDecoder (0x88982F50)
        /// </summary>
        /// <param name="bitmap"></param>
        /// <returns></returns>
        public async Task<OcrResult?> OcrAsync(Bitmap bitmap)
        {
            try
            {
                using var stream = new InMemoryRandomAccessStream();
                bitmap.Save(stream.AsStream(),
                    ImageFormat.Png); //choose the specific image format by your own bitmap source
                var decoder = await BitmapDecoder.CreateAsync(stream);
                var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
                var ocrResult = await Engine.RecognizeAsync(softwareBitmap);
                softwareBitmap.Dispose();
                return ocrResult;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return null;
            }
        }

        public async Task<OcrResult?> OcrAsyncByFile(Bitmap bitmap)
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp.png");
                bitmap.Save(path);
                var storageFile = await StorageFile.GetFileFromPathAsync(path);
                IRandomAccessStream randomAccessStream = await storageFile.OpenReadAsync();
                var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
                var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
                var ocrResult = await Engine.RecognizeAsync(softwareBitmap);
                softwareBitmap.Dispose();
                return ocrResult;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return null;
            }
        }

        public string Ocr(Bitmap bitmap)
        {
            var ocrResult = OcrAsync(bitmap);
            if (ocrResult.Result == null)
            {
                return "";
            }
            else
            {
                Debug.WriteLine("文字识别结果：" + ocrResult.Result.Text);
                return ocrResult.Result.Text;
            }
        }
    }
}