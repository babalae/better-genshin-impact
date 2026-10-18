using System.Diagnostics;
using Microsoft.Win32;

namespace Fischless.GameCapture.BitBlt;

public class BitBltRegistryHelper
{
    /// <summary>
    /// https://github.com/babalae/better-genshin-impact/issues/92
    /// Win11下 BitBlt截图方式不可用，需要关闭窗口优化功能，这是具体的注册表操作
    /// \HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences
    /// DirectXUserGlobalSettings = SwapEffectUpgradeEnable=0;
    ///
    /// 要在游戏启动前设置才有效
    /// </summary>
    public static void SetDirectXUserGlobalSettings()
    {
        try
        {
            const string keyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
            const string valueName = "DirectXUserGlobalSettings";
            const string settingName = "SwapEffectUpgradeEnable";
            const string settingData = "SwapEffectUpgradeEnable=0";

            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            if (key == null)
            {
                return;
            }

            var existingValue = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (existingValue is not null and not string)
            {
                return;
            }

            var valueData = existingValue as string ?? string.Empty;
            var settings = valueData.Split(';');
            var found = false;
            for (var i = 0; i < settings.Length; i++)
            {
                var separatorIndex = settings[i].IndexOf('=');
                if (separatorIndex < 0 || !string.Equals(settings[i][..separatorIndex].Trim(), settingName, StringComparison.Ordinal))
                {
                    continue;
                }

                settings[i] = settingData;
                found = true;
            }

            valueData = found
                ? string.Join(";", settings)
                : valueData + (valueData.Length > 0 && !valueData.EndsWith(';') ? ";" : string.Empty) + settingData + ";";

            if (valueData != existingValue as string)
            {
                var valueKind = existingValue is string ? key.GetValueKind(valueName) : RegistryValueKind.String;
                key.SetValue(valueName, valueData, valueKind);
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
    }
}
