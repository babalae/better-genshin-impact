using BetterGenshinImpact.Core.Recognition.ONNX;
using System.Collections.Concurrent;
using System.Globalization;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests.OCRTests
{
    public class PaddleFixture
    {
        private readonly ConcurrentDictionary<string, PaddleOcrService> _paddleOcrServices = new();

        public PaddleOcrService Get(string cultureInfoName = "zh-Hans", string version = "V5")
        {
            return _paddleOcrServices.GetOrAdd(cultureInfoName + "_" + version, _ =>
            {
                lock (_paddleOcrServices)
                {
                    if (version == "V5")
                    {
                        return new PaddleOcrService(new BgiOnnxFactory(new FakeLogger<BgiOnnxFactory>()),
                            PaddleOcrService.PaddleOcrModelType.FromCultureInfo(new CultureInfo(cultureInfoName)) ?? PaddleOcrService.PaddleOcrModelType.V5);
                    }
                    else if (version == "V4")
                    {
                        return new PaddleOcrService(new BgiOnnxFactory(new FakeLogger<BgiOnnxFactory>()),
                            PaddleOcrService.PaddleOcrModelType.FromCultureInfoV4(new CultureInfo(cultureInfoName)) ?? PaddleOcrService.PaddleOcrModelType.V4);
                    }
                    else
                    {
                        throw new NotSupportedException();
                    }
                }
            });
        }
    }
}