using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Vision.Recognition.Helper.OCR
{
    public class MediaOcr : IOcrService
    {
        private static readonly OcrEngine Engine =
            OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("zh-Hans-CN"));

        public async Task<OcrResult> OcrAsync(Bitmap bitmap)
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

        public string Ocr(Bitmap bitmap)
        {
            var ocrResult = OcrAsync(bitmap);
            return ocrResult.Result.Text;
        }
    }
}