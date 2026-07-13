using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests.OCRTests;
using BetterGenshinImpact.UnitTest.GameTaskTests.GetGridIconsTests;
using Microsoft.Extensions.Localization;
using Microsoft.ML.OnnxRuntime;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    [Collection("Init Collection")]
    public partial class BehavioursTests
    {
#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
        private static BgiYoloPredictor predictor;
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。

        private readonly PaddleFixture paddle; 
        private readonly IStringLocalizer<AutoFishingTask> stringLocalizer;
        private readonly InferenceSession session;
        private readonly Dictionary<string, float[]> prototypes;


        public BehavioursTests(PaddleFixture paddle, TorchFixture torch, LocalizationFixture localization, GridIconModelFixture iconModel)
        {
            this.paddle = paddle;
            this.useTorch = torch.UseTorch;
            this.stringLocalizer = localization.CreateStringLocalizer<AutoFishingTask>();
            this.session = iconModel.modelLoader.Value.session;
            this.prototypes = iconModel.modelLoader.Value.prototypes;
        }

        private IOcrService OcrService => paddle.Get();

        private static BgiYoloPredictor Predictor
        {
            get
            {
                return LazyInitializer.EnsureInitialized(ref predictor,
                    () => new BgiOnnxFactory(new FakeLogger<BgiOnnxFactory>())
                        .CreateYoloPredictor(BgiOnnxModel.BgiFish));
            }
        }

        private readonly bool useTorch;
    }
}