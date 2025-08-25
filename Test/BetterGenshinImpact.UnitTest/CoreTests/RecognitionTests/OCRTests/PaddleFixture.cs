using BetterGenshinImpact.Core.Recognition.ONNX;
using System.Collections.Concurrent;
using System.Globalization;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests.OCRTests
{
    public class PaddleFixture
    {
        private readonly ConcurrentDictionary<string, PaddleOcrService> _paddleOcrServices = new();

        public PaddleOcrService Get(string cultureInfoName = "zh-Hans")
        {
            return _paddleOcrServices.GetOrAdd(cultureInfoName, name =>
            {
                lock (_paddleOcrServices)
                {
                    return new PaddleOcrService(
                        new BgiOnnxFactory(new FakeLogger<BgiOnnxFactory>()),
                        PaddleOcrService.PaddleOcrModelType.FromCultureInfo(new CultureInfo(name)) ??
                        PaddleOcrService.PaddleOcrModelType.V5);
                }
            });
        }
    }
}