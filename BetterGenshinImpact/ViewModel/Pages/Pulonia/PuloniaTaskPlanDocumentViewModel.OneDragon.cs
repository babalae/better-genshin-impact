using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.ResinPlan;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Pulonia.Executors;
using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>一条龙参数面板与原任务树编辑器共用同一参数解析和撤销合同。</summary>
public partial class PuloniaTaskPlanDocumentViewModel
{
    /// <summary>刷新一条龙清单摘要，保留节点包装和选择；普通计划无需发送未使用的摘要通知。</summary>
    private void RefreshConfigurationSummaries()
    {
        if (Purpose != PuloniaTaskPlanPurpose.OneDragon) return;
        foreach (var node in EnumerateNodes()) node.NotifyConfigurationSummaryChanged();
    }

    /// <summary>生成清单配置说明；无法简化的分组和引用保留原结构。</summary>
    internal string GetOneDragonConfigurationSummary(PuloniaTaskNodeViewModel node)
    {
        if (!node.IsLeaf)
            return node.Model.Source?.Kind switch
            {
                "plan" => $"引用计划 · {node.Model.Source.PlanId} · 执行 {node.RepeatCount} 次",
                "directory" => $"{node.Model.Source.TaskType} 目录 · {node.Model.Source.Resource?.RelativePath ?? node.Model.Source.Path} · 执行 {node.RepeatCount} 次",
                _ => $"{node.Children.Count} 个步骤 · 整组执行 {node.RepeatCount} 次"
            };
        // 方案缺失的判定与运行准备共用解析器，预览不抛异常，只在摘要中提示。
        var resolution = ResolveParameters(node.Model, GetAncestorModels(node.Parent));
        var values = resolution.Parameters;
        var sharedSettings = DescribeSharedSettingsSummary(node, resolution);
        if (node.Model.TaskType == ResinPlanTaskType)
        {
            // 体力计划只列出用量与参与的任务；子任务的详细设置在设置区查看
            var parts = new List<string> { DescribeResinPlan(values) };
            if (node.IsEffectivelyEnabled && HasEnabledResinTask(node))
                parts.Add("⚠ 计划中另有消耗树脂的任务，会与体力计划共用树脂");
            if (sharedSettings is not null) parts.Add(sharedSettings);
            return string.Join(" · ", parts);
        }
        var keys = new[] { "party_name", "team_name", "team", "fight_team_name", "domain_name", "boss_name", "boss_num", "strategy_name", "combat_script", "country" };
        var capability = node.Model.TaskType switch
        {
            "solo.auto_domain" => ResinCapabilities.Domain,
            "solo.auto_stygian_onslaught" => ResinCapabilities.StygianOnslaught,
            "solo.auto_boss" => ResinCapabilities.Boss,
            "solo.auto_ley_line_outcrop" => ResinCapabilities.LeyLineOutcrop,
            _ => null
        };
        var summary = keys.Where(values.ContainsKey).Select(key =>
            $"{PuloniaTaskFieldNames.GetDisplayName(key)}：{DisplayValue(key)}").ToList();
        if (capability is not null)
        {
            // 摘要与卡片共用能力校验；缺失或 null 沿用任务默认值，格式错误仅在预览中提示。
            try
            {
                var usage = values["resin_usage"] is { Type: not JTokenType.Null } resin
                    ? SoloTaskJson.Deserialize<ResinUsage>(resin.ToString()) : capability.Defaults;
                summary.Add("树脂使用：" + ResinUsageText.Summary(usage, capability));
            }
            catch (System.Text.Json.JsonException)
            {
                summary.Add("树脂使用：参数格式无效");
            }
        }
        if ((capability is not null || node.Model.TaskType == CraftTaskType) && node.IsEffectivelyEnabled
            && EnumerateNodes().Any(other => other.IsLeaf && other.IsEffectivelyEnabled && other.Model.TaskType == ResinPlanTaskType))
            summary.Add("⚠ 与体力计划共用树脂");
        if (values["fight_config"] is JObject fight && !string.IsNullOrWhiteSpace(fight.Value<string>("StrategyName")))
            summary.Add("战斗策略：" + fight.Value<string>("StrategyName"));
        if (!string.IsNullOrWhiteSpace(node.Path)) summary.Add("资源：" + node.Path);
        if (sharedSettings is not null) summary.Add(sharedSettings);
        return string.Join(" · ", summary);

        // 树脂次数及方式由公共摘要解释，其余字段保留原始显示值。
        string DisplayValue(string key)
        {
            return values[key]?.Type == JTokenType.Null ? "默认" : values[key]!.ToString();
        }
    }

    /// <summary>
    /// 共享设置摘要：缺失方案优先提示，其次列出所挂方案和本任务覆盖数量；能力不使用共享设置或均未设置时为空。
    /// </summary>
    private string? DescribeSharedSettingsSummary(PuloniaTaskNodeViewModel node, PuloniaParameterResolution resolution)
    {
        if (resolution.Settings.MissingProfiles.Count > 0)
            return "⚠ 设置方案缺失";
        if (resolution.BoundSettingKeys.Count == 0)
            return null;
        var parts = new List<string>();
        if (node.Model.ProfileId is { } profileId && FindProfile(profileId) is { } profile)
            parts.Add("方案：" + profile.Name);
        if (node.Model.Settings.Count > 0)
            parts.Add($"共享设置：本任务覆盖 {node.Model.Settings.Count} 项");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>体力计划的任务类型。</summary>
    private static readonly string ResinPlanTaskType = PuloniaSoloTaskExecutor.ToTaskType(ResinPlanTask.TaskId);

    /// <summary>合成浓缩树脂的任务类型；它也会改变体力计划可用的原粹。</summary>
    private static readonly string CraftTaskType = PuloniaSoloTaskExecutor.ToTaskType(GoToCraftingBenchTask.TaskId);

    /// <summary>体力计划会调度的任务类型；同一计划中单独启用它们时会共用树脂。</summary>
    private static readonly string[] ResinTaskTypes =
    [
        CraftTaskType,
        PuloniaSoloTaskExecutor.ToTaskType(AutoDomainTask.TaskId),
        PuloniaSoloTaskExecutor.ToTaskType(AutoBossTask.TaskId),
        PuloniaSoloTaskExecutor.ToTaskType(AutoStygianOnslaughtTask.TaskId),
        PuloniaSoloTaskExecutor.ToTaskType(AutoLeyLineOutcropTask.TaskId)
    ];

    /// <summary>描述体力计划的用量、优先合成与参与的任务。</summary>
    /// <param name="values">体力计划的有效参数。</param>
    /// <returns>摘要文本。</returns>
    private static string DescribeResinPlan(JObject values)
    {
        var maxResin = values.Value<int?>("max_resin") ?? 0;
        var text = maxResin > 0 ? $"体力用量：最多 {maxResin} 点" : "体力用量：全部";
        if (values.Value<bool?>("craft_condensed_first") == true) text += " · 优先合成浓缩";
        var enabled = new (string Key, string Name, bool Default)[]
            {
                ("boss", "首领", false), ("domain", "秘境养成", true), ("stygian", "幽境危战", true), ("ley_line", "地脉花", true)
            }
            .Where(item => values[item.Key] is JObject sub ? sub.Value<bool?>("enabled") ?? item.Default : item.Default)
            .Select(item => item.Name)
            .ToArray();
        return text + (enabled.Length == 0 ? " · 未开启任何任务" : " · 参与：" + string.Join("、", enabled));
    }

    /// <summary>判断计划中除体力计划外是否还启用了消耗树脂的任务。</summary>
    /// <param name="resinPlan">体力计划节点。</param>
    /// <returns>存在时返回 true。</returns>
    private bool HasEnabledResinTask(PuloniaTaskNodeViewModel resinPlan)
        => EnumerateNodes().Any(other => !ReferenceEquals(other, resinPlan) && other.IsLeaf && other.IsEffectivelyEnabled
                                         && ResinTaskTypes.Contains(other.Model.TaskType));

    /// <summary>移除本任务的任务参数，使能力默认值重新生效；共享设置不受影响。</summary>
    /// <param name="node">叶子节点；分组忽略。</param>
    internal void RestoreParameterInheritance(PuloniaTaskNodeViewModel node)
    {
        if (!node.IsLeaf) return;
        EnsureEditableNode(node);
        ApplyMutation(() => node.Model.Parameters = new JObject(), node);
    }

    /// <summary>读取当前节点有效参数的独立副本，卡片草稿不会受开关刷新影响。</summary>
    internal JObject GetOneDragonParameters(PuloniaTaskNodeViewModel node)
        => ResolveEffectiveParameters(node.Model, GetAncestorModels(node.Parent), out _);

    /// <summary>
    /// 一条龙设置即时生效：用卡片当前输入换算出的显式参数替换节点参数，按能力 Schema 校验通过后才提交。
    /// 连续修改同一组字段（例如逐字输入）合并为一次撤销，也不改变当前选择。
    /// </summary>
    /// <param name="node">被编辑的节点。</param>
    /// <param name="parameters">节点新的显式参数；未出现的字段继续继承。</param>
    /// <returns>参数确有变化并已提交时返回 true。</returns>
    /// <exception cref="InvalidOperationException">节点已经不在当前计划中。</exception>
    /// <exception cref="PuloniaTaskValidationException">合并后的有效参数不符合 Schema。</exception>
    internal bool ReplaceOneDragonParameters(PuloniaTaskNodeViewModel node, JObject parameters)
    {
        EnsureEditableNode(node);
        var current = node.Model.Parameters;
        if (JToken.DeepEquals(current, parameters)) return false;

        // 1. 按替换后的显式参数重新解析有效值，确认结果仍符合能力 Schema
        var probe = new PuloniaTask
        {
            Id = node.Model.Id, Name = node.Model.Name, TaskType = node.Model.TaskType, Path = node.Model.Path,
            Resource = node.Model.Resource, ProfileId = node.Model.ProfileId,
            Settings = (JObject)node.Model.Settings.DeepClone(), Parameters = parameters
        };
        if (GetDefinition(node.Model) is { } definition)
            PuloniaTaskValidator.ValidateParameters(PreviewEffectiveParameters(probe, node.Parent!),
                definition.ParameterSchema, node.Name);

        // 2. 以变化的字段名作为合并标识，同一字段的连续输入只占用一次撤销
        var changedNames = current.Properties().Select(property => property.Name)
            .Union(parameters.Properties().Select(property => property.Name), StringComparer.Ordinal)
            .Where(name => !JToken.DeepEquals(current[name], parameters[name]))
            .OrderBy(name => name, StringComparer.Ordinal);
        ApplyMutation(() => node.Model.Parameters = (JObject)parameters.DeepClone(), null,
            "one-dragon-settings|" + node.Id + "|" + string.Join(",", changedNames));
        return true;
    }
}
