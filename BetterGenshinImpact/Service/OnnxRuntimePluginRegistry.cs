using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 管理当前进程中已经注册的 Plugin EP。ORT 要求在所有相关 Session 释放前不能卸载插件，
/// 因此这里有意不调用 UnregisterExecutionProviderLibrary，插件会保持到进程退出。
/// </summary>
public sealed class OnnxRuntimePluginRegistry(ILogger<OnnxRuntimePluginRegistry> logger)
    : IOnnxRuntimePluginRegistry
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, RegisteredPlugin> _registeredByEpName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _failedRegistrations = new(StringComparer.OrdinalIgnoreCase);

    public bool TryRegister(OnnxRuntimePluginResolution resolution, out IReadOnlyList<OrtEpDevice> devices,
        out string error)
    {
        lock (_syncRoot)
        {
            if (_registeredByEpName.TryGetValue(resolution.Descriptor.EpName, out var existing))
            {
                devices = existing.Devices;
                error = "";
                return true;
            }

            if (_failedRegistrations.TryGetValue(resolution.LibraryPath, out var previousError))
            {
                devices = [];
                error = previousError;
                return false;
            }

            if (!File.Exists(resolution.LibraryPath))
            {
                devices = [];
                error = $"Plugin EP 入口文件不存在：{resolution.LibraryPath}";
                _failedRegistrations[resolution.LibraryPath] = error;
                return false;
            }

            try
            {
                var env = OrtEnv.Instance();
                var registrationName = BuildRegistrationName(resolution);
                env.RegisterExecutionProviderLibrary(registrationName, resolution.LibraryPath);

                devices = env.GetEpDevices()
                    .Where(device => string.Equals(device.EpName, resolution.Descriptor.EpName,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (devices.Count == 0)
                {
                    error = $"插件已加载，但没有发现 {resolution.Descriptor.EpName} 可用设备。";
                    _failedRegistrations[resolution.LibraryPath] = error;
                    logger.LogWarning("[ONNX] {Error}", error);
                    return false;
                }

                error = "";
                _registeredByEpName[resolution.Descriptor.EpName] =
                    new RegisteredPlugin(resolution.LibraryPath, devices);
                logger.LogInformation("[ONNX] 已注册 Plugin EP {Plugin} {Version}，发现 {Count} 个设备",
                    resolution.Descriptor.DisplayName, resolution.Version, devices.Count);
                return true;
            }
            catch (Exception exception)
            {
                devices = [];
                error = exception.Message;
                _failedRegistrations[resolution.LibraryPath] = error;
                logger.LogError(exception, "[ONNX] 注册 Plugin EP {Plugin} {Version} 失败",
                    resolution.Descriptor.DisplayName, resolution.Version);
                return false;
            }
        }
    }

    public IReadOnlyList<OrtEpDevice> GetDevices(string epName)
    {
        lock (_syncRoot)
        {
            return _registeredByEpName
                .Where(pair => string.Equals(pair.Key, epName, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Value)
                .SelectMany(plugin => plugin.Devices)
                .Distinct()
                .ToArray();
        }
    }

    private static string BuildRegistrationName(OnnxRuntimePluginResolution resolution)
    {
        var raw = $"bettergi_{resolution.Descriptor.Id}_{resolution.Version}";
        return string.Concat(raw.Select(character => char.IsLetterOrDigit(character) ? character : '_'));
    }

    private sealed record RegisteredPlugin(string LibraryPath, IReadOnlyList<OrtEpDevice> Devices);
}
