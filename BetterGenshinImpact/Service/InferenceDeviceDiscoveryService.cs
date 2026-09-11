using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;

namespace BetterGenshinImpact.Service;

public sealed class InferenceDeviceDiscoveryService(
    IConfigService configService,
    IOnnxRuntimePluginManager pluginManager,
    IOnnxRuntimePluginRegistry pluginRegistry,
    ILogger<InferenceDeviceDiscoveryService> logger)
    : IInferenceDeviceDiscoveryService
{
    public Task<IReadOnlyList<InferenceDeviceDescriptor>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Discover(cancellationToken), cancellationToken);
    }

    private IReadOnlyList<InferenceDeviceDescriptor> Discover(CancellationToken cancellationToken)
    {
        var result = new List<InferenceDeviceDescriptor>
        {
            new(InferenceDeviceType.Cpu, "cpu:0", "CPU（内置）", InferenceHardwareType.Cpu,
                "CPU", 0, "CPU")
        };

        result.AddRange(DxgiAdapterEnumerator.Enumerate(logger));
        cancellationToken.ThrowIfCancellationRequested();

        var config = configService.Get().HardwareAccelerationConfig;
        OnnxRuntimePathHelper.Prepare(config, logger);
        RegisterInstalledPlugins(InferenceDeviceType.OpenVino, config, cancellationToken);
        RegisterInstalledPlugins(InferenceDeviceType.Cuda, config, cancellationToken);

        var openVinoDevices = pluginRegistry.GetDevices("OpenVINOExecutionProvider")
            .Where(device => device.HardwareDevice.Type != OrtHardwareDeviceType.NPU ||
                             IsIntelDevice(device.HardwareDevice))
            .ToArray();
        var openVinoInstalled = pluginManager.GetCurrentPackages().Any(package =>
            package.Descriptor.Provider == InferenceDeviceType.OpenVino &&
            !string.IsNullOrWhiteSpace(package.InstalledVersion));
        if (openVinoDevices.Length > 0)
        {
            var discoveredTypes = openVinoDevices
                .Select(device => device.HardwareDevice.Type)
                .ToHashSet();
            var autoTypes = new[]
                {
                    (OrtHardwareDeviceType.NPU, "NPU"),
                    (OrtHardwareDeviceType.GPU, "GPU"),
                    (OrtHardwareDeviceType.CPU, "CPU")
                }
                .Where(item => discoveredTypes.Contains(item.Item1))
                .Select(item => item.Item2)
                .ToArray();
            result.Add(new InferenceDeviceDescriptor(
                InferenceDeviceType.OpenVino,
                "openvino:auto",
                $"OpenVINO 自动选择（{string.Join("/", autoTypes)}）",
                autoTypes.Contains("NPU") ? InferenceHardwareType.Npu : InferenceHardwareType.Gpu,
                "Intel",
                0,
                autoTypes.Length == 0 ? "AUTO" : $"AUTO:{string.Join(",", autoTypes)}"));
            result.AddRange(openVinoDevices.Select(device => ToDescriptor(device)));
        }
        else if (!openVinoInstalled)
        {
            result.Add(new InferenceDeviceDescriptor(
                InferenceDeviceType.OpenVino,
                "openvino:not-installed",
                "安装 Plugin EP 后发现设备",
                InferenceHardwareType.Npu,
                "Intel",
                0,
                "AUTO",
                false,
                "请先安装 OpenVINO Plugin EP。"));
        }

        if (openVinoInstalled && openVinoDevices.All(device =>
                device.HardwareDevice.Type != OrtHardwareDeviceType.NPU))
        {
            result.Add(new InferenceDeviceDescriptor(
                InferenceDeviceType.OpenVino,
                "openvino:npu-unavailable",
                "Intel NPU（未发现）",
                InferenceHardwareType.Npu,
                "Intel",
                0,
                "NPU",
                false,
                "未发现 Intel NPU，或当前驱动/固件不满足 OpenVINO NPU 插件要求。"));
        }

        var nvidiaGpus = NvidiaSmiDeviceEnumerator.Enumerate(cancellationToken);
        var cudaDevices = pluginRegistry.GetDevices("CUDAExecutionProvider");
        if (cudaDevices.Count > 0)
        {
            result.AddRange(cudaDevices.Select(device => ToDescriptor(device, nvidiaGpus)));
        }
        else
        {
            if (nvidiaGpus.Count > 0)
            {
                result.AddRange(nvidiaGpus.Select(gpu => new InferenceDeviceDescriptor(
                    InferenceDeviceType.Cuda,
                    $"Cuda:{gpu.Uuid}",
                    $"{gpu.Name}（CUDA Plugin EP 未加载）",
                    InferenceHardwareType.Gpu,
                    "NVIDIA",
                    gpu.DeviceId,
                    gpu.DeviceId.ToString(CultureInfo.InvariantCulture),
                    false,
                    "请先安装匹配的 CUDA Plugin EP，并确认 CUDA/cuDNN 依赖可用。")));
            }
            else
            {
                result.Add(new InferenceDeviceDescriptor(
                    InferenceDeviceType.Cuda,
                    "cuda:unavailable",
                    "未发现可用 NVIDIA GPU",
                    InferenceHardwareType.Gpu,
                    "NVIDIA",
                    0,
                    "0",
                    false,
                    "未发现 NVIDIA 驱动/GPU，或 CUDA Plugin EP 及其 CUDA/cuDNN 依赖不可用。"));
            }
        }

        return result
            .GroupBy(device => (device.Provider, device.StableId))
            .Select(group => group.First())
            .ToArray();
    }

    private void RegisterInstalledPlugins(InferenceDeviceType provider, HardwareAccelerationConfig config,
        CancellationToken cancellationToken)
    {
        foreach (var resolution in pluginManager.ResolveForStartup(provider, config.CudaRuntime))
        {
            cancellationToken.ThrowIfCancellationRequested();
            OnnxRuntimePathHelper.AppendPluginDirectory(resolution.LibraryPath, logger);
            if (pluginRegistry.TryRegister(resolution, out _, out _))
            {
                return;
            }
        }
    }

    private static InferenceDeviceDescriptor ToDescriptor(
        OrtEpDevice device, IReadOnlyList<NvidiaGpuInfo>? nvidiaGpus = null)
    {
        var hardware = device.HardwareDevice;
        var type = hardware.Type switch
        {
            OrtHardwareDeviceType.NPU => InferenceHardwareType.Npu,
            OrtHardwareDeviceType.GPU => InferenceHardwareType.Gpu,
            _ => InferenceHardwareType.Cpu
        };
        var provider = string.Equals(device.EpName, "CUDAExecutionProvider",
            StringComparison.OrdinalIgnoreCase)
            ? InferenceDeviceType.Cuda
            : InferenceDeviceType.OpenVino;
        var metadata = hardware.Metadata.Entries;
        var name = GetMetadata(metadata, "device_name", "name", "description", "full_name");
        var nvidiaGpu = provider == InferenceDeviceType.Cuda
            ? nvidiaGpus?.FirstOrDefault(gpu => gpu.DeviceId == hardware.DeviceId)
            : null;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = nvidiaGpu?.Name ?? $"{hardware.Vendor} {hardware.Type} {hardware.DeviceId}";
        }

        var uuid = GetMetadata(metadata, "uuid", "device_uuid");
        if (string.IsNullOrWhiteSpace(uuid))
        {
            uuid = nvidiaGpu?.Uuid ?? "";
        }
        var stableId = !string.IsNullOrWhiteSpace(uuid)
            ? $"{provider}:{uuid}"
            : $"{provider}:{hardware.VendorId:X4}:{hardware.Type}:{hardware.DeviceId}";
        var option = provider == InferenceDeviceType.Cuda
            ? hardware.DeviceId.ToString(CultureInfo.InvariantCulture)
            : ToOpenVinoOption(hardware);

        return new InferenceDeviceDescriptor(provider, stableId, name, type,
            hardware.Vendor, hardware.DeviceId, option);
    }

    private static string ToOpenVinoOption(OrtHardwareDevice hardware)
    {
        return hardware.Type switch
        {
            OrtHardwareDeviceType.NPU => "NPU",
            OrtHardwareDeviceType.GPU when hardware.DeviceId > 0 => $"GPU.{hardware.DeviceId}",
            OrtHardwareDeviceType.GPU => "GPU",
            _ => "CPU"
        };
    }

    private static bool IsIntelDevice(OrtHardwareDevice hardware)
    {
        return hardware.VendorId == 0x8086 ||
               hardware.Vendor.Contains("Intel", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetMetadata(IReadOnlyDictionary<string, string> metadata, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "";
    }

}

internal sealed record NvidiaGpuInfo(uint DeviceId, string Name, string Uuid);

internal static class NvidiaSmiDeviceEnumerator
{
    public static IReadOnlyList<NvidiaGpuInfo> Enumerate(CancellationToken cancellationToken = default)
    {
        var result = new List<NvidiaGpuInfo>();
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "nvidia-smi.exe",
                    Arguments = "--query-gpu=index,name,uuid --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };
            process.Start();
            if (!process.WaitForExit(3000))
            {
                process.Kill(true);
                return result;
            }

            var output = process.StandardOutput.ReadToEnd();
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(',', 3, StringSplitOptions.TrimEntries);
                if (parts.Length != 3 || !uint.TryParse(parts[0], out var index))
                {
                    continue;
                }

                result.Add(new NvidiaGpuInfo(index, parts[1], parts[2]));
            }
        }
        catch
        {
            // 没有 NVIDIA 驱动或 nvidia-smi 时不添加 CUDA 设备。
        }

        return result;
    }
}

internal static class DxgiAdapterEnumerator
{
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private const uint DxgiAdapterFlagSoftware = 2;
    private static readonly Guid Factory1Guid = new("770AAE78-F26F-4DBA-A829-253C83D1B387");

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(in Guid riid, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(IntPtr self, uint adapterIndex, out IntPtr adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(IntPtr self, out DxgiAdapterDesc1 description);

    public static IReadOnlyList<InferenceDeviceDescriptor> Enumerate(ILogger logger)
    {
        var result = new List<InferenceDeviceDescriptor>();
        IntPtr factory = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(CreateDXGIFactory1(Factory1Guid, out factory));
            var enumAdapters = GetVTableDelegate<EnumAdapters1Delegate>(factory, 12);
            for (uint index = 0; ; index++)
            {
                var hr = enumAdapters(factory, index, out var adapter);
                if (hr == DxgiErrorNotFound)
                {
                    break;
                }

                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    var getDescription = GetVTableDelegate<GetDesc1Delegate>(adapter, 10);
                    Marshal.ThrowExceptionForHR(getDescription(adapter, out var description));
                    if ((description.Flags & DxgiAdapterFlagSoftware) != 0)
                    {
                        continue;
                    }

                    var luid = $"{description.AdapterLuid.HighPart:X8}:{description.AdapterLuid.LowPart:X8}";
                    var memoryGb = description.DedicatedVideoMemory / 1024d / 1024 / 1024;
                    var displayName = memoryGb >= 0.1
                        ? $"{description.Description}（{memoryGb:0.#} GB，DML {index}）"
                        : $"{description.Description}（DML {index}）";
                    result.Add(new InferenceDeviceDescriptor(
                        InferenceDeviceType.GpuDirectMl,
                        $"Dml:{luid}",
                        displayName,
                        InferenceHardwareType.Gpu,
                        $"0x{description.VendorId:X4}",
                        index,
                        index.ToString(CultureInfo.InvariantCulture)));
                }
                finally
                {
                    Marshal.Release(adapter);
                }
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "[ONNX] 枚举 DirectML/DXGI 设备失败");
            result.Add(new InferenceDeviceDescriptor(
                InferenceDeviceType.GpuDirectMl,
                "Dml:0",
                "DirectML 默认设备（DML 0）",
                InferenceHardwareType.Gpu,
                "DirectML",
                0,
                "0"));
        }
        finally
        {
            if (factory != IntPtr.Zero)
            {
                Marshal.Release(factory);
            }
        }

        return result;
    }

    public static int ResolveDeviceId(string stableId, int fallback, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(stableId))
        {
            return fallback;
        }

        var match = Enumerate(logger).FirstOrDefault(device =>
            device.StableId.Equals(stableId, StringComparison.OrdinalIgnoreCase));
        return match is null ? fallback : checked((int)match.DeviceId);
    }

    private static T GetVTableDelegate<T>(IntPtr instance, int slot) where T : Delegate
    {
        var vTable = Marshal.ReadIntPtr(instance);
        var method = Marshal.ReadIntPtr(vTable, slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(method);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSystemId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public Luid AdapterLuid;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }
}
