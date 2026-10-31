using BetterGenshinImpact.Core.Recognition.ONNX;
using System.Collections.Concurrent;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests.OCRTests
{
    public class PaddleFixture
    {
        private readonly ConcurrentDictionary<string, PaddleOcrService> _paddleOcrServices = new();

        /// <summary>
        /// 获取纯 CPU 的 OCR 服务。主程序只内置 PP-OCRv6，V4、V5 及多语言模型已移入插件，
        /// 两个参数保留只为兼容现有用例，不再影响使用的模型。
        /// </summary>
        public PaddleOcrService Get(string cultureInfoName = "zh-Hans", string version = "V6")
        {
            return _paddleOcrServices.GetOrAdd("V6", _ =>
            {
                lock (_paddleOcrServices)
                {
                    return new PaddleOcrService(new BgiOnnxSessionFactory(new FakeLogger<BgiOnnxFactory>(),
                            ResolvedInferenceRuntime.Cpu()),
                        PaddleOcrService.PaddleOcrModelType.V6);
                }
            });
        }
    }
}
