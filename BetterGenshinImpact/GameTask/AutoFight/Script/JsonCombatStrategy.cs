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

    /// <summary>
    /// 动作执行后确保E技能进入冷却（释放成功），至多重试3次
    /// </summary>
    public bool EnsureCast { get; set; } = false;

    /// <summary>
    /// 额外优先级条目：同一动作在不同条件下有不同的优先级位置
    /// 主循环会将这些条目与原动作一起展开，按 priority 排序后依次检查
    /// </summary>
    public List<JsonMorePriority> MorePriorities { get; set; } = [];
}

/// <summary>
/// 条件对象
/// </summary>
public class JsonCondition
{
    public string Expression { get; set; } = string.Empty;
}

/// <summary>
/// 额外优先级条目
/// </summary>
public class JsonMorePriority
{
    public string Expression { get; set; } = string.Empty;
    public int Priority { get; set; }
}
