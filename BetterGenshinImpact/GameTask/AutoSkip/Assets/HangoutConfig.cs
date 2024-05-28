using System;
using BetterGenshinImpact.Model;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.GameTask.AutoSkip.Assets;

public class HangoutConfig : Singleton<HangoutConfig>
{
    public Dictionary<string, List<string>> HangoutOptions;
    public List<string> HangoutOptionsTitleList;

    private HangoutConfig()
    {
        // 邀约分支选项
        string hangoutJson = File.ReadAllText(Global.Absolute(@"GameTask\AutoSkip\Assets\hangout.json"));
        HangoutOptions = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(hangoutJson,
            ConfigService.JsonOptions) ?? throw new Exception("hangout.json deserialize failed");
        HangoutOptionsTitleList = new List<string>(HangoutOptions.Keys);
    }
}
