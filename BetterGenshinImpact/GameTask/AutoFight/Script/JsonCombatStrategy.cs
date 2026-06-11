using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoFight.Script;

/// <summary>
/// JSON 战斗策略 — 顶层模型
/// </summary>
public class JsonCombatStrategy
{
    public JsonInfo Info { get; set; } = new();
    public List<JsonAction> Actions { get; set; } = [];

    /// <summary>
    /// 从所有动作节点的 Character 字段合并去重，得到策略涉及的角色列表
    /// </summary>
    public List<string> GetCharacterNames()
    {
        return Actions
            .Select(a => a.Character)
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct()
            .ToList();
    }
}

public class JsonInfo
{
    public string Name { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public Dictionary<string, object>? Config { get; set; }
    public List<string> PreActions { get; set; } = [];
}

/// <summary>
/// 单个动作
/// </summary>
public class JsonAction
{
    public string Name { get; set; } = string.Empty;
    public string Character { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public JsonCondition Condition { get; set; } = new();
    public int Index { get; set; }
}

/// <summary>
/// 条件对象
/// </summary>
public class JsonCondition
{
    public string Expression { get; set; } = string.Empty;
    public List<JsonSubCondition> SubConditions { get; set; } = [];
}

/// <summary>
/// 具名条件子项
/// </summary>
public class JsonSubCondition
{
    public string Name { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
}
