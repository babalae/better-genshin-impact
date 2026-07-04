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
    /// <summary>
    /// 从文件解析 JSON 战斗策略
    /// </summary>
    /// <param name="path">策略文件路径</param>
    /// <returns>解析后的战斗策略</returns>
    /// <exception cref="FileNotFoundException">文件不存在</exception>
    /// <exception cref="InvalidOperationException">解析失败或格式错误</exception>
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

    /// <summary>
    /// 从 JSON 字符串解析战斗策略
    /// </summary>
    /// <param name="json">JSON 字符串</param>
    /// <returns>解析后的战斗策略</returns>
    /// <exception cref="InvalidOperationException">解析失败或格式错误</exception>
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

        if (strategy.Info == null)
        {
            Logger.LogError("JSON 战斗策略缺少 Info 节点");
            throw new InvalidOperationException("JSON 战斗策略缺少 Info 节点");
        }

        if (strategy.Actions == null || strategy.Actions.Count == 0)
        {
            Logger.LogError("JSON 战斗策略缺少 Actions 节点或动作为空");
            throw new InvalidOperationException("JSON 战斗策略中未定义任何动作");
        }

        // 校验 action index 唯一性
        ValidateActions(strategy.Actions);

        Logger.LogInformation("JSON 战斗策略加载完成：{Name}，共 {Count} 个动作",
            strategy.Info.Name, strategy.Actions.Count);

        return strategy;
    }

    /// <summary>校验动作索引唯一性</summary>
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
