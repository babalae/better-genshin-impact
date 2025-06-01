using BetterGenshinImpact.GameTask.AutoFishing;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TorchSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    public class TorchFixture
    {
        private readonly Lazy<TorchLoader> torch = new Lazy<TorchLoader>();
        public bool UseTorch
        {
            get
            {
                return torch.Value.UseTorch;
            }
        }
    }

    internal class TorchLoader
    {
        public TorchLoader()
        {
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
                UseTorch = true;
            }
            catch (Exception e) when (e is DllNotFoundException || e is NotSupportedException)
            {
                UseTorch = false;
            }
        }

        public bool UseTorch { get; private set; }
    }
}
