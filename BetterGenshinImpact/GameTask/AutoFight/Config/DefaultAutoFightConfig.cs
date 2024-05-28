using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BetterGenshinImpact.GameTask.AutoFight.Config;

public class DefaultAutoFightConfig
{
    public static List<CombatAvatar> CombatAvatars { get; set; }
    public static List<string> CombatAvatarNames { get; set; }
    public static Dictionary<string, CombatAvatar> CombatAvatarMap { get; set; }
    public static Dictionary<string, CombatAvatar> CombatAvatarNameEnMap { get; set; }

    static DefaultAutoFightConfig()
    {
        var json = File.ReadAllText(Global.Absolute(@"GameTask\AutoFight\Assets\combat_avatar.json"));
        var config = JsonSerializer.Deserialize<List<CombatAvatar>>(json, ConfigService.JsonOptions);
        CombatAvatars = config ?? throw new Exception("combat_avatar.json deserialize failed");
        CombatAvatarNames = config.Select(x => x.Name).ToList();
        CombatAvatarMap = config.ToDictionary(x => x.Name);
        CombatAvatarNameEnMap = config.ToDictionary(x => x.NameEn);
    }

    public static string AvatarAliasToStandardName(string alias)
    {
        var avatar = CombatAvatars.Find(x => x.Alias.Contains(alias));
        if (avatar == null)
        {
            throw new Exception($"角色名称校验失败：{alias}");
        }

        return avatar.Name;
    }
}
