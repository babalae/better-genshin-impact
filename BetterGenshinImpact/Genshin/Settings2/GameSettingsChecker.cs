using BetterGenshinImpact.Helpers;
﻿using System;
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
                TaskControl.Logger.LogDebug(Lang.S["Gen_11888_f93a70"]);
                return;
            }

            GenshinGameSettings? settings = GenshinGameSettings.Parse(settingStr);
            if (settings == null)
            {
                TaskControl.Logger.LogDebug(Lang.S["Gen_11888_f93a70"]);
                return;
            }

            GenshinGameInputSettings? inputSettings = GenshinGameInputSettings.Parse(settings.InputData);
            if (inputSettings == null)
            {
                TaskControl.Logger.LogError(Lang.S["Gen_11893_b43a8d"]);
                return;
            }
            
            if (settings.GammaValue != "2.200000047683716")
            {
                TaskControl.Logger.LogError(Lang.S["Gen_11892_97c3f3"]);
            }

            if (inputSettings.MouseSenseIndex != 2
                || inputSettings.MouseSenseIndexY != 2
                || inputSettings.MouseFocusSenseIndex != 2
                || inputSettings.MouseFocusSenseIndexY != 2)
            {
                TaskControl.Logger.LogInformation(Lang.S["Gen_11891_fda1e9"],
                    inputSettings.MouseSenseIndex + 1, inputSettings.MouseSenseIndexY + 1,
                    inputSettings.MouseFocusSenseIndex + 1, inputSettings.MouseFocusSenseIndexY + 1);
                TaskControl.Logger.LogError(Lang.S["Gen_11890_559efc"]);
            }

            var lang = (TextLanguage)settings.DeviceLanguageType;
            if (lang != TextLanguage.SimplifiedChinese)
            {
                TaskControl.Logger.LogWarning(Lang.S["Gen_11889_aabc33"], lang);
            }
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, Lang.S["Gen_11888_f93a70"]);
        }
    }
}