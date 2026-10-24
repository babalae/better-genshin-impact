using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 按任务类型解释节点结果：各执行器的关键结果键、核验边界与资源类别都在这里描述，界面只负责格式化展示。
/// 未识别的扩展字段原样保留，不丢弃扩展执行器的数据。
/// </summary>
public static class PuloniaTaskResultInterpreter
{
    /// <summary>
    /// 奖励汇总在结果数据中的键，对应 RewardSummary 按 SoloTaskJson 序列化后的 items。
    /// </summary>
    public const string RewardItemsKey = "items";

    /// <summary>
    /// Shell 执行器的关键结果字段；缺失时标记为未返回，不冒充退出码 0 或空输出。
    /// </summary>
    private static readonly (string Key, string Label)[] ShellKeyFields =
    [
        ("exit_code", "进程退出码"), ("standard_output", "标准输出"), ("standard_error", "标准错误"),
        ("standard_output_truncated", "标准输出已截断"), ("standard_error_truncated", "标准错误已截断")
    ];

    /// <summary>
    /// 地图追踪采集 CD 的结果字段；只在执行器实际写入时展示。
    /// </summary>
    private static readonly string[] CollectionKeys =
    [
        "collection_material", "collection_rule", "collection_rule_source", "collection_last_success_at",
        "collection_next_eligible_at", "collection_basis"
    ];

    /// <summary>
    /// 各类型结果的核验边界，避免把脚本返回或路线结束解释为已领取/采集成功。
    /// </summary>
    public static string GetVerificationNote(string taskType) => taskType switch
    {
        "pathing" => "文件级 CD 按整份文件执行成功估算采集完成，不记录部分成功；不代表逐点采集数量或实际收益。",
        "javascript" => "旧 JS 脚本正常返回不等于游戏副作用已核验；仅展示执行器实际返回的数据与证据。",
        "keymouse" => "宏回放结束不等于游戏目标完成；录制文件本身没有结构化收益证据。",
        "shell" => "退出码表示进程结果；标准输出/错误每路最多保留 65536 字符，截断会明确标记。",
        "csharp" => "返回值与确认事件来自注册的 C# 能力；局部确认不等于整个节点成功。",
        _ when PuloniaSoloTaskExecutor.IsSoloTaskType(taskType) =>
            "独立任务的结果种类与原因码由任务本身给出；“已执行，未核验”表示任务没有确认手段，需要核对游戏状态。奖励只列出任务识别到的物品，未识别时不推算收益。",
        _ => "没有返回领取/合成数量时不推算收益；“已执行，未核验”需要核对游戏状态。"
    };

    /// <summary>
    /// 快照节点资源路径的中文标签。
    /// </summary>
    public static string GetResourcePathLabel(string taskType) => taskType switch
    {
        "pathing" => "路线文件", "javascript" => "JS 项目目录", "keymouse" => "录制文件", _ => "资源路径"
    };

    /// <summary>
    /// 分类型挑选关键结果并保持展示顺序：原因码、奖励、类型关键字段，最后是其余全部字段。
    /// </summary>
    /// <param name="result">节点尝试结果。</param>
    /// <returns>按展示顺序排列的结果项；执行器没有附带任何结构化数据时为空列表。</returns>
    public static IReadOnlyList<PuloniaTaskResultEntry> ReadEntries(PuloniaTaskNodeResult result)
    {
        var entries = new List<PuloniaTaskResultEntry>();
        var handled = new HashSet<string>(StringComparer.Ordinal);
        var data = result.Data;
        // 1. 执行器给出的原因码放在最前，便于按原因筛查失败与跳过
        if (!string.IsNullOrWhiteSpace(result.ReasonCode))
            entries.Add(new PuloniaTaskResultEntry("原因码", PuloniaTaskResultEntryKind.Value, new JValue(result.ReasonCode)));
        // 2. 奖励汇总（RewardSummary 的 items）由界面逐行展示为“名称 ×数量”
        if (TryReadRewards(result, out var rewards))
        {
            handled.Add(RewardItemsKey);
            entries.Add(new PuloniaTaskResultEntry("奖励", PuloniaTaskResultEntryKind.Rewards, rewards));
        }
        // 3. 各类型的关键结果
        switch (result.TaskType)
        {
            case "shell":
                foreach (var (key, label) in ShellKeyFields)
                    AddKeyField(entries, handled, data, key, label);
                break;
            case "pathing":
                AddPathingFields(entries, handled, data);
                break;
            case "csharp":
                if (data.ContainsKey("sum"))
                {
                    AddKeyField(entries, handled, data, "sum", "计算合计");
                    AddKeyField(entries, handled, data, "value_count", "参与计算的数值数量");
                }
                break;
        }
        // 4. 其余字段全部保留，未登记的扩展键使用原字段名
        foreach (var property in data.Properties().Where(item => !handled.Contains(item.Name)))
            entries.Add(new PuloniaTaskResultEntry(PuloniaTaskFieldNames.GetDisplayName(property.Name),
                PuloniaTaskResultEntryKind.Value, property.Value));
        return entries;
    }

    /// <summary>
    /// 解释地图追踪结果：实际执行的路线按 is_success 展示完整执行、失败原因与拾取统计；
    /// 没有执行结果的节点（执行器异常、因采集 CD 跳过）只标记“路线完整执行”未返回，并展示 CD 字段。
    /// </summary>
    private static void AddPathingFields(List<PuloniaTaskResultEntry> entries, HashSet<string> handled, JObject data)
    {
        if (data.ContainsKey("is_success"))
        {
            AddKeyField(entries, handled, data, "is_success", "路线完整执行");
            AddKeyField(entries, handled, data, "success_fight_count", "成功战斗次数");
            handled.Add("error_message");
            entries.Add(data["error_message"] is { Type: not JTokenType.Null } error
                ? new PuloniaTaskResultEntry("失败原因", PuloniaTaskResultEntryKind.Value, error)
                : new PuloniaTaskResultEntry("失败原因", data.Value<bool>("is_success")
                    ? PuloniaTaskResultEntryKind.NoFailure : PuloniaTaskResultEntryKind.FailureNotProvided));
            handled.Add("revive_count");
            handled.Add("collected_items");
            entries.Add(data["revive_count"] is { Type: not JTokenType.Null } revive
                ? new PuloniaTaskResultEntry("复活次数", PuloniaTaskResultEntryKind.Value, revive)
                : new PuloniaTaskResultEntry("复活次数", PuloniaTaskResultEntryKind.NotCounted));
            entries.Add(data["collected_items"] is JObject items
                ? new PuloniaTaskResultEntry("拾取物品", PuloniaTaskResultEntryKind.CollectedItems, items)
                : new PuloniaTaskResultEntry("拾取物品", PuloniaTaskResultEntryKind.NotCounted));
        }
        else
            // 执行器异常或因 CD 跳过时没有执行结果，明确标记未返回，不推断成功或失败。
            AddKeyField(entries, handled, data, "is_success", "路线完整执行");
        foreach (var key in CollectionKeys.Where(data.ContainsKey))
            AddKeyField(entries, handled, data, key, PuloniaTaskFieldNames.GetDisplayName(key));
    }

    /// <summary>
    /// 判断结果数据中的 items 能否按奖励解释。
    /// 独立任务的 items 一律按奖励解释；其他类型只在每一项都带 name 时才按奖励解释，避免误读扩展数据。
    /// </summary>
    /// <param name="result">节点结果。</param>
    /// <param name="rewards">可以按奖励解释的数组；无法解释时为 null。</param>
    /// <returns>能够按奖励解释时返回 true。</returns>
    private static bool TryReadRewards(PuloniaTaskNodeResult result, out JArray? rewards)
    {
        rewards = null;
        if (result.Data[RewardItemsKey] is not JArray items)
            return false;
        var isSoloTask = PuloniaSoloTaskExecutor.IsSoloTaskType(result.TaskType);
        var isRewardShaped = items.All(item => item is JObject entry && entry["name"] is { Type: JTokenType.String });
        if (!isRewardShaped || (items.Count == 0 && !isSoloTask))
            return false;
        rewards = items;
        return true;
    }

    /// <summary>
    /// 登记一个关键结果字段；缺失时标记为未返回，不把缺失 bool 或退出码解释为 false 或 0。
    /// </summary>
    private static void AddKeyField(List<PuloniaTaskResultEntry> entries, HashSet<string> handled,
        JObject data, string key, string label)
    {
        handled.Add(key);
        entries.Add(data.TryGetValue(key, out var value)
            ? new PuloniaTaskResultEntry(label, PuloniaTaskResultEntryKind.Value, value)
            : new PuloniaTaskResultEntry(label, PuloniaTaskResultEntryKind.NotReturned));
    }
}
