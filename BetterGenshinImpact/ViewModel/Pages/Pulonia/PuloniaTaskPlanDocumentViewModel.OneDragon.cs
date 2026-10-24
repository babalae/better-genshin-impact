using System;
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
        // 预设缺失/不兼容的判定与运行准备共用解析器，预览不抛异常，只在摘要中提示。
        var resolution = ResolveParameters(node.Model, GetAncestorModels(node.Parent));
        var values = resolution.Parameters;
        var preset = resolution.PresetStatus switch
        {
            PuloniaPresetStatus.None => "能力默认 / 本计划设置",
            PuloniaPresetStatus.Missing => "⚠ 预设缺失",
            PuloniaPresetStatus.Incompatible => "⚠ 预设不兼容",
            _ => resolution.Preset!.Name
        };
        var keys = new[] { "party_name", "team_name", "team", "fight_team_name", "domain_name", "boss_name", "boss_num", "strategy_name", "combat_script", "run_count", "count", "round_count", "country" };
        var summary = keys.Where(values.ContainsKey).Select(key =>
            $"{PuloniaTaskFieldNames.GetDisplayName(key)}：{DisplayValue(key)}").ToList();
        if (values["fight_config"] is JObject fight && !string.IsNullOrWhiteSpace(fight.Value<string>("StrategyName")))
            summary.Add("战斗策略：" + fight.Value<string>("StrategyName"));
        if (!string.IsNullOrWhiteSpace(node.Path)) summary.Add("资源：" + node.Path);
        summary.Add("预设：" + preset);
        if (values["weekday_overrides"] is JObject weekly && weekly.Count > 0) summary.Add("已配置每周安排（配置中查看）");
        return string.Join(" · ", summary);

        // 次数参数的 0 和树脂耗尽模式有业务语义，清单不能把它们显示成执行零次或一次。
        string DisplayValue(string key)
        {
            if (key == "round_count" && values.Value<int?>(key) == 0) return "按树脂配置";
            if (key == "run_count" && values.Value<bool?>("specify_run_count") == false) return "原粹树脂用尽";
            if (key == "count" && values.Value<bool?>("is_resin_exhaustion_mode") == true) return "树脂用尽";
            return values[key]?.Type == JTokenType.Null ? "默认" : values[key]!.ToString();
        }
    }

    /// <summary>移除显式参数，使能力默认值、预设及父组配置重新生效。</summary>
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
            Resource = node.Model.Resource, PresetId = node.Model.PresetId, Parameters = parameters
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
