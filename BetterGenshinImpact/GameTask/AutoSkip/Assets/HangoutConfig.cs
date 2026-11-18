using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BetterGenshinImpact.GameTask.AutoSkip.Assets;

public class HangoutConfig : Singleton<HangoutConfig>
{
    /// <summary>
    /// 角色 -> 结局 -> 该结局的选项关键词
    /// </summary>
    public Dictionary<string, Dictionary<string, List<string>>> HangoutOptions;

    /// <summary>
    /// 全部角色名
    /// </summary>
    public List<string> HangoutCharacters;

    /// <summary>
    /// 角色 -> 该角色的结局名列表
    /// </summary>
    public Dictionary<string, List<string>> HangoutEndings;

    private HangoutConfig()
    {
        // 邀约分支选项
        string hangoutJson = File.ReadAllText(Global.Absolute(@"GameTask\AutoSkip\Assets\hangout.json"));
        HangoutOptions = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<string>>>>(hangoutJson,
            ConfigService.JsonOptions) ?? throw new Exception("hangout.json deserialize failed");
        HangoutCharacters = new List<string>(HangoutOptions.Keys);
        HangoutEndings = HangoutOptions.ToDictionary(pair => pair.Key, pair => new List<string>(pair.Value.Keys));
    }

    public List<string>? GetOptions(string character, string ending)
    {
        if (string.IsNullOrEmpty(character) || string.IsNullOrEmpty(ending))
        {
            return null;
        }

        return HangoutOptions.TryGetValue(character, out var endings)
               && endings.TryGetValue(ending, out var options)
            ? options
            : null;
    }
}
