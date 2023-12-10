using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Genshin.Settings;

public class SettingsContainer
{
    protected MainJson? data = null;
    public LanguageSettings? Language;
    public ResolutionSettings? Resolution;
    public InputDataSettings? InputData;
    public OverrideControllerSettings? OverrideController;

    public SettingsContainer()
    {
        FromReg();
    }

    public void FromReg()
    {
        if (GenshinRegistry.GetRegistryKey() is not { } hk)
        {
            return;
        }

        using (hk)
        {
            string value_name = SearchRegistryName(hk);
            if (hk.GetValue(value_name) is not byte[] rawBytes)
            {
                return;
            }

            unsafe
            {
                // Keep the rawBytes pinned when parsing
                fixed (byte* ptr = rawBytes)
                {
                    Parse(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(ptr));
                }
            }
        }
    }

    private void Parse(ReadOnlySpan<byte> rawCfg)
    {
        try
        {
            data = JsonSerializer.Deserialize<MainJson>(rawCfg, new JsonSerializerOptions()
            {
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            });

            if (data is null)
            {
                return;
            }

            Language = new LanguageSettings(data);
            Resolution = new ResolutionSettings();
            InputData = new InputDataSettings(data);
            OverrideController = new OverrideControllerSettings(data);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
    }

    private static string SearchRegistryName(RegistryKey key)
    {
        string value_name = string.Empty;
        string[] names = key.GetValueNames();

        foreach (string name in names)
        {
            if (name.Contains("GENERAL_DATA"))
            {
                value_name = name;
                break;
            }
        }

        if (value_name == string.Empty)
        {
            throw new ArgumentException(value_name);
        }

        return value_name;
    }
}
