using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests.OCRTests;
using Microsoft.Extensions.Configuration;
using System.Runtime.InteropServices;
using TorchSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    [Collection("Paddle Collection")]
    public partial class BehavioursTests
    {
#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
        private static BgiYoloPredictor predictor;
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。

        private readonly PaddleFixture paddle;
        public BehavioursTests(PaddleFixture paddle)
        {
            this.paddle = paddle;

            var configuration = new ConfigurationBuilder().AddUserSecrets<BehavioursTests>().Build();
            if (configuration == null)
            {
                throw new NullReferenceException();
            }
            string torchDllFullPath = configuration["torchDllFullPath"] ?? throw new NullReferenceException();
            try
            {
                NativeLibrary.Load(torchDllFullPath);
                if (torch.TryInitializeDeviceType(DeviceType.CUDA))
                {
                    torch.set_default_device(new torch.Device(DeviceType.CUDA));
                }
                this.useTorch = true;
            }
            catch (Exception e) when (e is DllNotFoundException || e is NotSupportedException)
            {
                this.useTorch = false;
            }
        }

        private IOcrService OcrService => paddle.Get();

        private static BgiYoloPredictor Predictor
        {
            get
            {
                return LazyInitializer.EnsureInitialized(ref predictor, () => new BgiOnnxFactory(new Core.Config.HardwareAccelerationConfig(), new FakeLogger<BgiOnnxFactory>()).CreateYoloPredictor(BgiOnnxModel.BgiFish));
            }
        }

        private readonly bool useTorch;
    }
}
