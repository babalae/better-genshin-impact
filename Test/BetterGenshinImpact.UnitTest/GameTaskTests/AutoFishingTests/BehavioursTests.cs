using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask.AutoFishing;
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
            // 需要读取主项目编译目录中的配置
            string configFullPath = Path.Combine(Path.GetFullPath(@"..\..\..\..\..\"), @"BetterGenshinImpact\bin\x64\Debug\net8.0-windows10.0.22621.0\User\config.json");
            IConfigurationRoot configurationRoot = new ConfigurationBuilder().AddJsonFile(configFullPath, optional: false).Build();
            AutoFishingConfig autoFishingConfig = configurationRoot.GetRequiredSection("autoFishingConfig").Get<AutoFishingConfig>() ?? throw new ArgumentNullException();
            try
            {
                NativeLibrary.Load(autoFishingConfig.TorchDllFullPath);
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
                return LazyInitializer.EnsureInitialized(ref predictor, () => new BgiOnnxFactory(new HardwareAccelerationConfig(), new FakeLogger<BgiOnnxFactory>()).CreateYoloPredictor(BgiOnnxModel.BgiFish));
            }
        }

        private readonly bool useTorch;
    }
}
