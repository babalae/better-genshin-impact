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
            const string valueData = "SwapEffectUpgradeEnable=0;";

            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            key.SetValue(valueName, valueData, RegistryValueKind.String);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
    }
}
