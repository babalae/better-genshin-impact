using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Genshin.Settings;

public class SettingsContainer
{
    private readonly GenshinRegistryType _registryType;
    private readonly string? _gameExecutablePath;
    protected MainJson? data = null;
    public LanguageSettings? Language;
    public ResolutionSettings? Resolution;
    public InputDataSettings? InputData;
    public OverrideControllerSettings? OverrideController;

    public SettingsContainer(GenshinRegistryType registryType = GenshinRegistryType.Auto, string? gameExecutablePath = null)
    {
        _registryType = registryType;
        _gameExecutablePath = gameExecutablePath;

        try
        {
            FromReg();
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "读取原神注册表信息出错");
        }
    }

    public void FromReg()
    {
        if (GenshinRegistry.GetRegistryKey(_registryType, _gameExecutablePath) is not { } hk)
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

            // 在托管数组范围内查找字符串结束位置，避免注册表数据缺少空终止符时越界读取
            ReadOnlySpan<byte> rawCfg = rawBytes;
            int nullTerminatorIndex = rawCfg.IndexOf((byte)0);
            if (nullTerminatorIndex >= 0)
            {
                rawCfg = rawCfg[..nullTerminatorIndex];
            }

            Parse(rawCfg);
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
            Resolution = new ResolutionSettings(_registryType, _gameExecutablePath);
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
