using System;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Genshin.Settings;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Genshin.Settings2;

public class GameSettingsChecker
{
    public static void LoadGameSettingsAndCheck()
    {
        try
        {
            var settingStr = GenshinGameSettings.GetStrFromRegistry();
            if (settingStr == null)
            {
                TaskControl.Logger.LogDebug("获取原神游戏设置失败");
                return;
            }

            GenshinGameSettings? settings = GenshinGameSettings.Parse(settingStr);
            if (settings == null)
            {
                TaskControl.Logger.LogDebug("获取原神游戏设置失败");
                return;
            }

            GenshinGameInputSettings? inputSettings = GenshinGameInputSettings.Parse(settings.InputData);
            if (inputSettings == null)
            {
                TaskControl.Logger.LogError("获取原神游戏输入设置失败");
                return;
            }
            
            if (settings.GammaValue != "2.200000047683716")
            {
                TaskControl.Logger.LogError("检测到游戏亮度非默认值，将会影响功能正常使用，请在原神 游戏设置——图像——亮度 中恢复默认亮度！");
            }

            if (inputSettings.MouseSenseIndex != 2
                || inputSettings.MouseSenseIndexY != 2
                || inputSettings.MouseFocusSenseIndex != 2
                || inputSettings.MouseFocusSenseIndexY != 2)
            {
                TaskControl.Logger.LogInformation("当前：镜头水平灵敏度{X1}，镜头垂直灵敏度{Y1}，镜头水平灵敏度（瞄准模式）{X2}，镜头垂直灵敏度（瞄准模式）{Y2}",
                    inputSettings.MouseSenseIndex + 1, inputSettings.MouseSenseIndexY + 1,
                    inputSettings.MouseFocusSenseIndex + 1, inputSettings.MouseFocusSenseIndexY + 1);
                TaskControl.Logger.LogError("检测到镜头灵敏度不是默认值3，将会影响所有视角移动功能的正常使用，请在原神 游戏设置——控制 中恢复默认灵敏度！");
            }

            var lang = (TextLanguage)settings.DeviceLanguageType;
            if (lang != TextLanguage.SimplifiedChinese)
            {
                TaskControl.Logger.LogWarning("当前游戏语言{Lang}不是简体中文，部分功能可能无法正常使用。The game language is not Simplified Chinese, some functions may not work properly", lang);
            }
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "获取原神游戏设置失败");
        }
    }
}