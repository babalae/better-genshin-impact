using BetterGenshinImpact.Core.Config;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

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
        var config = Newtonsoft.Json.JsonConvert.DeserializeObject<IEnumerable<CombatAvatar>>(json) ?? throw new Exception("combat_avatar.json deserialize failed");
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
    
    // //添加自定义角色-假装识别够用了，后续再考虑
    // public static void AddCombatAvatar(CombatAvatar newAvatar)
    // {
    //     // 读取现有的 JSON 文件内容
    //     var json = File.ReadAllText(Global.Absolute(@"GameTask\AutoFight\Assets\combat_avatar.json"));
    //     var combatAvatars = Newtonsoft.Json.JsonConvert.DeserializeObject<List<CombatAvatar>>(json) ?? new List<CombatAvatar>();
    //
    //     // 添加新角色
    //     combatAvatars.Add(newAvatar);
    //
    //     // 将更新后的角色列表序列化回 JSON 格式
    //     var updatedJson = Newtonsoft.Json.JsonConvert.SerializeObject(combatAvatars, Newtonsoft.Json.Formatting.Indented);
    //
    //     // 将更新后的 JSON 写回到文件
    //     File.WriteAllText(Global.Absolute(@"GameTask\AutoFight\Assets\combat_avatar.json"), updatedJson);
    // }
    
}
