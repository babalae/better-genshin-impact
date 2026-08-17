using TorchSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    public class TorchFixture
    {
        private static readonly Lazy<TorchLoader> Torch = new();

        internal static bool IsTorchAvailable
        {
            get
            {
                return Torch.Value.UseTorch;
            }
        }
    }

    internal sealed class TorchFactAttribute : FactAttribute
    {
        public TorchFactAttribute()
        {
            if (!TorchFixture.IsTorchAvailable)
            {
                Skip = "当前环境未安装可用的 TorchSharp 后端";
            }
        }
    }

    internal sealed class TorchTheoryAttribute : TheoryAttribute
    {
        public TorchTheoryAttribute()
        {
            if (!TorchFixture.IsTorchAvailable)
            {
                Skip = "当前环境未安装可用的 TorchSharp 后端";
            }
        }
    }

    internal class TorchLoader
    {
        public TorchLoader()
        {
            if (!HasNativeBackend())
            {
                return;
            }

            try
            {
                torch.InitializeDeviceType(DeviceType.CPU);
                UseTorch = true;
            }
            catch (Exception e) when (e is BadImageFormatException or DllNotFoundException or NotSupportedException or TypeInitializationException)
            {
                UseTorch = false;
            }
        }

        private static bool HasNativeBackend()
        {
            var backendFileName = OperatingSystem.IsWindows()
                ? "torch_cpu.dll"
                : OperatingSystem.IsMacOS() ? "libtorch_cpu.dylib" : "libtorch_cpu.so";
            var runtimeFolder = OperatingSystem.IsWindows()
                ? "win-x64"
                : OperatingSystem.IsMacOS() ? "osx-x64" : "linux-x64";

            if (File.Exists(Path.Combine(AppContext.BaseDirectory, backendFileName)) ||
                File.Exists(Path.Combine(AppContext.BaseDirectory, "runtimes", runtimeFolder, "native", backendFileName)))
            {
                return true;
            }

            var path = Environment.GetEnvironmentVariable("PATH");
            return !string.IsNullOrWhiteSpace(path) && path
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(directory => File.Exists(Path.Combine(directory, backendFileName)));
        }

        public bool UseTorch { get; private set; }
    }
}
