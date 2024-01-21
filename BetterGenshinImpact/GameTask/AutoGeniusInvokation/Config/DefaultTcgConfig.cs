using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation.Config;

public class DefaultTcgConfig
{

    public static List<CharacterCard> CharacterCards { get; set; }
    public static Dictionary<string, CharacterCard> CharacterCardMap { get; set; }

    static DefaultTcgConfig()
    {
        var json = File.ReadAllText(Global.Absolute(@"GameTask\AutoGeniusInvokation\Assets\tcg_character_card.json"));
        var config = JsonSerializer.Deserialize<List<CharacterCard>>(json, ConfigService.JsonOptions);
        CharacterCards = config ?? throw new System.Exception("tcg_character_card.json deserialize failed");
        CharacterCardMap = config.ToDictionary(x => x.Name);
    }
}