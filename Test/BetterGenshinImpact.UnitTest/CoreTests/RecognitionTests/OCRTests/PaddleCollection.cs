using BetterGenshinImpact.Core.Recognition.OCR;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests.OCRTests
{
    [CollectionDefinition("Paddle Collection")]
    public class PaddleCollection : ICollectionFixture<PaddleFixture>
    {
    }

    public class PaddleFixture
    {
        private readonly ConcurrentDictionary<string, PaddleOcrService> paddleOcrServices = new ConcurrentDictionary<string, PaddleOcrService>();

        public PaddleOcrService Get(string cultureInfoName = "zh-Hans")
        {
            return paddleOcrServices.GetOrAdd(cultureInfoName, name => { lock (paddleOcrServices) { return new PaddleOcrService(name); } });
        }
    }
}
