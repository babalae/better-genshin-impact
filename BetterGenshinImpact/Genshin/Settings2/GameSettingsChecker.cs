using System;
using System.IO;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Genshin.Settings2;

public class GameSettingsChecker
{
    public static bool? LoadGameSettingsAndCheck(string? path = null)
    {
        var settingStr = GenshinGameSettings.GetStrFromRegistry();
        if (settingStr == null)
        {
            TaskControl.Logger.LogError("获取游戏设置失败");
            MessageBox.Error("获取游戏设置失败");
            return null;
        }

        GenshinGameSettings? settings = GenshinGameSettings.Parse(settingStr);
        if (settings == null)
        {
            TaskControl.Logger.LogError("获取游戏设置失败");
            MessageBox.Error("获取游戏设置失败");
            return null;
        }

        GenshinGameInputSettings? inputSettings = GenshinGameInputSettings.Parse(settings.InputData);
        if (inputSettings == null)
        {
            TaskControl.Logger.LogError("获取游戏输入设置失败");
            MessageBox.Error("获取游戏输入设置失败");
            return null;
        }

        try
        {
            if (settings.GammaValue != "2.200000047683716")
            {
                throw new Exception("检测到游戏亮度不是默认值，请将游戏亮度调整为默认值！");
            }

            if (inputSettings.MouseSenseIndex != 2
                || inputSettings.MouseSenseIndexY != 2
                || inputSettings.MouseFocusSenseIndex != 2
                || inputSettings.MouseFocusSenseIndexY != 2)
            {
                throw new Exception("检测到镜头灵敏度不是默认值，请将镜头灵敏度调整为默认值！");
            }
        }
        catch (Exception e)
        {
            MessageBox.Error(e.Message);
            if (path != null)
            {
                File.WriteAllText(path, settingStr);
            }

            return false;
        }

        return true;
    }
}