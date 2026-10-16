using BetterGenshinImpact.Core.Config;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;

namespace BetterGenshinImpact.GameTask.AutoFight.Config;

public class DefaultAutoFightConfig
{
    //public static List<CombatAvatar> CombatAvatars { get; set; }
    public static List<string> CombatAvatarNames { get; set; }
    public static Dictionary<string, CombatAvatar> CombatAvatarMap { get; set; }
    public static Dictionary<string, CombatAvatar> CombatAvatarNameEnMap { get; set; }
    public static FrozenDictionary<string, string> CombatAvatarAliasToNameMap { get; set; }

    static DefaultAutoFightConfig()
    {
        var json = File.ReadAllText(Global.Absolute(@"GameTask\AutoFight\Assets\combat_avatar.json"));
        var config = Newtonsoft.Json.JsonConvert.DeserializeObject<List<CombatAvatar>>(json) ?? throw new Exception("combat_avatar.json deserialize failed");
        foreach (var avatar in config)
        {
            if (avatar.ElementalType is { } element && !Enum.IsDefined(element))
            {
                throw new Exception($"combat_avatar.json：{avatar.Name} 的元素配置无效");
            }

            if ((avatar.NormalAttack || avatar.ElementalSkill) && (avatar.ElementalType == null || avatar.ElementalType == ElementalType.Omni))
            {
                throw new Exception($"combat_avatar.json：{avatar.Name} 启用元素采集前必须配置角色元素");
            }
        }
        //CombatAvatars = config;
        CombatAvatarNames = config.Select(x => x.Name).ToList();
        CombatAvatarMap = config.ToDictionary(x => x.Name);
        CombatAvatarNameEnMap = config.ToDictionary(x => x.NameEn);
        CombatAvatarAliasToNameMap = config.SelectMany(c => c.Alias.Select(a => new KeyValuePair<string, string>(a, c.Name))).ToFrozenDictionary();
    }

    public static string AvatarAliasToStandardName(string alias)
    {
        if (CombatAvatarAliasToNameMap.TryGetValue(alias, out string? name) && name != null)
        {
            return name;
        }
        throw new Exception($"角色名称校验失败：{alias}");

        // return CombatAvatars.Find(x => x.Alias.Contains(alias))?.Name ?? throw new Exception($"角色名称校验失败：{alias}");
    }
}
