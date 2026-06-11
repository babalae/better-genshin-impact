using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoFight.Script;

/// <summary>
/// JSON 战斗策略解析器
/// </summary>
public static class JsonCombatStrategyParser
{
    public static JsonCombatStrategy ParseFile(string path)
    {
        if (!File.Exists(path))
        {
            Logger.LogError("JSON 战斗策略文件不存在：{Path}", path);
            throw new FileNotFoundException("JSON 战斗策略文件不存在", path);
        }

        var json = File.ReadAllText(path);
        return Parse(json);
    }

    public static JsonCombatStrategy Parse(string json)
    {
        JsonCombatStrategy? strategy;
        try
        {
            strategy = JsonConvert.DeserializeObject<JsonCombatStrategy>(json);
        }
        catch (JsonException ex)
        {
            Logger.LogError("JSON 战斗策略解析失败：{Msg}", ex.Message);
            throw new InvalidOperationException($"JSON 战斗策略格式错误：{ex.Message}", ex);
        }

        if (strategy == null)
        {
            Logger.LogError("JSON 战斗策略反序列化结果为空");
            throw new InvalidOperationException("JSON 战斗策略反序列化失败");
        }

        // 校验 action index 唯一性
        ValidateActions(strategy.Actions);

        Logger.LogInformation("JSON 战斗策略加载完成：{Name}，共 {Count} 个动作",
            strategy.Info.Name, strategy.Actions.Count);

        return strategy;
    }

    private static void ValidateActions(List<JsonAction> actions)
    {
        var seen = new HashSet<int>();
        foreach (var action in actions)
        {
            if (!seen.Add(action.Index))
            {
                Logger.LogError("JSON 战斗策略中存在重复的 index：{Index}", action.Index);
                throw new InvalidOperationException($"JSON 战斗策略中存在重复的 index：{action.Index}");
            }
        }
    }
}
