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
            try
            {
                NativeLibrary.Load(@"C:\torch\lib\torch_cpu.dll");
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
