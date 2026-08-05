using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        // 校验动作合法性（名称需能作为条件标识符解析；index 允许重复）
        ValidateActions(strategy.Actions);

        Logger.LogInformation("JSON 战斗策略加载完成：{Name}，共 {Count} 个动作",
            strategy.Info.Name, strategy.Actions.Count);

        return strategy;
    }

    /// <summary>
    /// 校验动作名称合法性。
    /// 动作名必须能作为条件表达式中的单个标识符解析（复用 <see cref="ConditionEvaluator.IsValidActionName"/>：
    /// 不能是布尔字面量 true/false、纯数字，不能含空白、逗号、运算符等，也不能与内置条件函数同名）。
    /// 允许不同动作使用相同 index（since/count 等按 index 查询时取最近一次执行的事件记录）。
    /// </summary>
    private static void ValidateActions(List<JsonAction> actions)
    {
        var actionNames = actions.Where(a => !string.IsNullOrEmpty(a.Name)).Select(a => a.Name).ToList();
        foreach (var action in actions)
        {
            if (!string.IsNullOrEmpty(action.Name) && !ConditionEvaluator.IsValidActionName(action.Name, actionNames))
            {
                Logger.LogError("JSON 战斗策略中动作名称无法作为条件标识符解析（不能是布尔字面量、纯数字，不能含空白、逗号、运算符等，也不能与内置条件函数同名）：{Name}", action.Name);
                throw new InvalidOperationException($"JSON 战斗策略中动作名称无法作为条件标识符解析：{action.Name}");
            }
        }
    }
}
