using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace BetterGenshinImpact.Genshin.Settings;

internal class SettingsContainer
{
    protected MainJson? data = null;
    public LanguageSettings? Language;
    public ResolutionSettings? Resolution;
    public InputDataSettings? InputData;

    public SettingsContainer()
    {
    }

    public void Parse(string rawCfg)
    {
        try
        {
            rawCfg = rawCfg.Replace('\0'.ToString(), "");
            data = JsonSerializer.Deserialize<MainJson>(rawCfg, new JsonSerializerOptions()
            {
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            });
            Language = new LanguageSettings(data);
            Resolution = new ResolutionSettings();
            InputData = new InputDataSettings(data);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
    }

    public void FromReg()
    {
        var rawCfg = RegistryContainer.Load();
        Parse(rawCfg);
    }
}

internal class RegistryContainer
{
    public static string Load()
    {
        using RegistryKey hk = GenshinRegistry.GetRegistryKey();
        string value_name = SearchName(hk);
        string raw_settings = Encoding.UTF8.GetString((byte[])hk.GetValue(value_name)!);
        return raw_settings;
    }

    private static string SearchName(RegistryKey key)
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