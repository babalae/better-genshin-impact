using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Service.Hdr;

public static class UserGpuPreferencesParser
{
    public const string UserGpuPreferencesRegistryPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    public const string GlobalSettingsValueName = "DirectXUserGlobalSettings";
    public const string AutoHdrValueName = "AutoHDREnable";
    public const string AutoHdrEnabledValue = "2097";
    public const string AutoHdrDisabledValue = "2096";

    public static IReadOnlyDictionary<string, string> Parse(string? rawValue)
    {
        Dictionary<string, string> settings = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return settings;
        }

        foreach (string segment in rawValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separatorIndex = segment.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex >= segment.Length - 1)
            {
                continue;
            }

            string key = segment[..separatorIndex].Trim();
            string value = segment[(separatorIndex + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0)
            {
                continue;
            }

            settings[key] = value;
        }

        return settings;
    }

    public static AutoHdrState GetAutoHdrState(string? rawValue)
    {
        IReadOnlyDictionary<string, string> settings = Parse(rawValue);
        if (!settings.TryGetValue(AutoHdrValueName, out string? autoHdrValue))
        {
            return AutoHdrState.Unset;
        }

        return autoHdrValue switch
        {
            AutoHdrEnabledValue => AutoHdrState.Enabled,
            AutoHdrDisabledValue => AutoHdrState.Disabled,
            _ => AutoHdrState.Unknown,
        };
    }

    public static AutoHdrState ResolveEffectiveState(AutoHdrState appState, AutoHdrState globalState)
    {
        return appState switch
        {
            AutoHdrState.Enabled => AutoHdrState.Enabled,
            AutoHdrState.Disabled => AutoHdrState.Disabled,
            AutoHdrState.Unknown => AutoHdrState.Unknown,
            _ => globalState,
        };
    }
}
