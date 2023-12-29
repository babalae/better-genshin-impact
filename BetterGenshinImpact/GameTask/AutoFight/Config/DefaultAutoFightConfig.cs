using BetterGenshinImpact.Core.Config;
using SharpCompress.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.GameTask.AutoFight.Config;

public class DefaultAutoFightConfig
{
    public static List<CombatAvatar> CombatAvatars { get; set; }
    public static List<string> CombatAvatarNames { get; set; }

    static DefaultAutoFightConfig()
    {
        var json = File.ReadAllText(Global.Absolute(@"GameTask\AutoFight\Assets\combat_avatar.json"));
        var config = JsonSerializer.Deserialize<List<CombatAvatar>>(json, ConfigService.JsonOptions);
        CombatAvatars = config ?? throw new Exception("combat_avatar.json deserialize failed");
        CombatAvatarNames = config.Select(x => x.Name).ToList();
    }
}