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

/// <summary>
/// 策略元信息
/// </summary>
public class JsonInfo
{
    /// <summary>策略名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>作者</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>自定义配置</summary>
    public Dictionary<string, object>? Config { get; set; }

    /// <summary>战斗前动作列表</summary>
    public List<string> PreActions { get; set; } = [];
}

/// <summary>
/// 单个动作节点
/// </summary>
public class JsonAction
{
    /// <summary>
    /// 动作名称（可选）：用于日志输出、按名称查询执行历史（since/count/last-exec）及条件词法的连字符合并；
    /// 不允许与内置条件函数同名，不允许包含逗号或无法作为条件标识符解析的字符（解析策略时校验）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>执行动作的角色名（为空时使用当前角色）</summary>
    public string Character { get; set; } = string.Empty;

    /// <summary>动作指令字符串（如：keydown(VK_LBUTTON),wait(1),keyup(VK_LBUTTON)）</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>触发条件</summary>
    public JsonCondition Condition { get; set; } = new();

    /// <summary>动作索引（用于排序和引用）</summary>
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
/// 动作触发条件
/// </summary>
public class JsonCondition
{
    /// <summary>条件表达式（如：t>5 && q-ready()）</summary>
    public string Expression { get; set; } = string.Empty;
}

/// <summary>
/// 额外优先级条目：同一动作在不同条件下有不同的优先级位置
/// </summary>
public class JsonMorePriority
{
    /// <summary>条件表达式</summary>
    public string Expression { get; set; } = string.Empty;

    /// <summary>优先级值（越小越优先）</summary>
    public int Priority { get; set; }
}
