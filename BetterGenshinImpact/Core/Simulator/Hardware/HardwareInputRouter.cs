using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Simulator.Hardware;

internal sealed class HardwareInputRouter
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, IHardwareKeyboardBackend> _keyboardBackends = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IHardwareMouseBackend> _mouseBackends = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missingKeyboardPortWarnings = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missingMousePortWarnings = new(StringComparer.OrdinalIgnoreCase);
    private readonly HardwareBackendFactory _backendFactory = new();
    private readonly ILogger? _logger = App.GetService<ILogger<HardwareInputRouter>>();

    public static HardwareInputRouter Instance { get; } = new();

    public IHardwareKeyboardBackend? GetKeyboardBackend()
    {
        var config = GetHardwareConfig();
        if (config == null || !config.IsKeyboardHardware)
        {
            return null;
        }

        return GetKeyboardBackend(config);
    }

    public IHardwareMouseBackend? GetMouseBackend()
    {
        var config = GetHardwareConfig();
        if (config == null || !config.IsMouseHardware)
        {
            return null;
        }

        return GetMouseBackend(config);
    }

    public string? GetKeyboardConnectedBaudRateText()
    {
        var config = GetHardwareConfig();
        if (config == null || !config.IsKeyboardHardware || config.IsKeyboardFerrumNetworkApi)
        {
            return null;
        }

        var cacheKey = BuildKeyboardCacheKey(config);
        if (cacheKey == null)
        {
            return null;
        }

        lock (_syncRoot)
        {
            return _keyboardBackends.TryGetValue(cacheKey, out var backend) && backend is IHardwareConnectionInfoProvider infoProvider
                ? infoProvider.BaudRateText
                : null;
        }
    }

    public string? GetMouseConnectedBaudRateText()
    {
        var config = GetHardwareConfig();
        if (config == null || !config.IsMouseHardware || config.IsMouseFerrumNetworkApi)
        {
            return null;
        }

        var cacheKey = BuildMouseCacheKey(config);
        if (cacheKey == null)
        {
            return null;
        }

        lock (_syncRoot)
        {
            return _mouseBackends.TryGetValue(cacheKey, out var backend) && backend is IHardwareConnectionInfoProvider infoProvider
                ? infoProvider.BaudRateText
                : null;
        }
    }

    private IHardwareKeyboardBackend? GetKeyboardBackend(HardwareInputConfig config)
    {
        var cacheKey = BuildKeyboardCacheKey(config);
        if (cacheKey == null)
        {
            return null;
        }

        lock (_syncRoot)
        {
            if (!_keyboardBackends.TryGetValue(cacheKey, out var backend))
            {
                backend = _backendFactory.CreateKeyboardBackend(config);
                _keyboardBackends[cacheKey] = backend;
            }

            return backend.EnsureConnected() ? backend : null;
        }
    }

    private IHardwareMouseBackend? GetMouseBackend(HardwareInputConfig config)
    {
        var cacheKey = BuildMouseCacheKey(config);
        if (cacheKey == null)
        {
            return null;
        }

        lock (_syncRoot)
        {
            if (!_mouseBackends.TryGetValue(cacheKey, out var backend))
            {
                backend = _backendFactory.CreateMouseBackend(config);
                _mouseBackends[cacheKey] = backend;
            }

            return backend.EnsureConnected() ? backend : null;
        }
    }

    private void WarnMissingPort(string vendor, bool isKeyboard)
    {
        lock (_syncRoot)
        {
            var warnings = isKeyboard ? _missingKeyboardPortWarnings : _missingMousePortWarnings;
            if (!warnings.Add(vendor))
            {
                return;
            }
        }

        _logger?.LogWarning(
            "Hardware {DeviceType} input is enabled for {Vendor}, but no COM port is available.",
            isKeyboard ? "keyboard" : "mouse",
            vendor);
    }

    private void WarnMissingNetwork(string vendor, string protocol, bool isKeyboard)
    {
        var warningKey = $"{vendor}|{protocol}|network";

        lock (_syncRoot)
        {
            var warnings = isKeyboard ? _missingKeyboardPortWarnings : _missingMousePortWarnings;
            if (!warnings.Add(warningKey))
            {
                return;
            }
        }

        _logger?.LogWarning(
            "Hardware {DeviceType} input is enabled for {Vendor} {Protocol}, but IP, port, or credential is missing.",
            isKeyboard ? "keyboard" : "mouse",
            vendor,
            protocol);
    }

    private string? BuildKeyboardCacheKey(HardwareInputConfig config)
    {
        if (config.IsKeyboardFerrumNetworkApi)
        {
            if (!HasNetworkEndpoint(config.KeyboardFerrumNetIp, config.KeyboardFerrumNetPort, config.KeyboardFerrumNetUuid))
            {
                WarnMissingNetwork(config.KeyboardHardwareVendor, config.KeyboardFerrumApi, isKeyboard: true);
                return null;
            }

            return $"keyboard|{config.KeyboardHardwareVendor}|{config.KeyboardFerrumApi}|{config.KeyboardFerrumNetIp.Trim()}|{config.KeyboardFerrumNetPort.Trim()}|{config.KeyboardFerrumNetUuid.Trim()}";
        }

        if (string.IsNullOrWhiteSpace(config.KeyboardEffectiveComPort))
        {
            WarnMissingPort(config.KeyboardHardwareVendor, isKeyboard: true);
            return null;
        }

        return $"keyboard|{config.KeyboardHardwareVendor}|{config.KeyboardEffectiveComPort.Trim()}";
    }

    private string? BuildMouseCacheKey(HardwareInputConfig config)
    {
        if (config.IsMouseFerrumNetworkApi)
        {
            if (!HasNetworkEndpoint(config.MouseFerrumNetIp, config.MouseFerrumNetPort, config.MouseFerrumNetUuid))
            {
                WarnMissingNetwork(config.MouseHardwareVendor, config.MouseFerrumApi, isKeyboard: false);
                return null;
            }

            return $"mouse|{config.MouseHardwareVendor}|{config.MouseFerrumApi}|{config.MouseFerrumNetIp.Trim()}|{config.MouseFerrumNetPort.Trim()}|{config.MouseFerrumNetUuid.Trim()}";
        }

        if (string.IsNullOrWhiteSpace(config.MouseEffectiveComPort))
        {
            WarnMissingPort(config.MouseHardwareVendor, isKeyboard: false);
            return null;
        }

        return $"mouse|{config.MouseHardwareVendor}|{config.MouseEffectiveComPort.Trim()}";
    }

    private static bool HasNetworkEndpoint(string ip, string port, string uuid)
    {
        return !string.IsNullOrWhiteSpace(ip)
            && !string.IsNullOrWhiteSpace(port)
            && !string.IsNullOrWhiteSpace(uuid);
    }

    private static HardwareInputConfig? GetHardwareConfig()
    {
        if (ConfigService.Config != null)
        {
            return ConfigService.Config.HardwareInputConfig;
        }

        var configService = App.GetService<IConfigService>();
        return configService?.Get().HardwareInputConfig;
    }
}
