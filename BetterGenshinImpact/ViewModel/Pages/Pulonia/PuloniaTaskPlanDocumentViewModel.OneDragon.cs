using System;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using CommunityToolkit.Mvvm.Input;
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
                "directory" => $"{node.Model.Source.TaskType} 目录 · {node.Model.Source.Path} · 执行 {node.RepeatCount} 次",
                _ => $"{node.Children.Count} 个步骤 · 整组执行 {node.RepeatCount} 次"
            };
        var values = GetOneDragonParameters(node);
        var selectedPreset = _presets.FirstOrDefault(p => p.Id == node.Model.PresetId);
        var preset = node.Model.PresetId is null ? "能力默认 / 本计划设置"
            : selectedPreset is null ? "⚠ 预设缺失"
            : selectedPreset.SchemaVersion != (GetDefinition(node.Model)?.SchemaVersion ?? 1)
              || !MatchesScope(selectedPreset.TaskType, selectedPreset.ResourceId, node.Model) ? "⚠ 预设不兼容"
            : selectedPreset.Name;
        var keys = new[] { "party_name", "team_name", "team", "fight_team_name", "domain_name", "boss_name", "boss_num", "strategy_name", "combat_script", "run_count", "count", "round_count", "country" };
        var summary = keys.Where(values.ContainsKey).Select(key =>
            $"{PuloniaTaskParameterFieldViewModel.GetDisplayName(key)}：{DisplayValue(key)}").ToList();
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
    [RelayCommand]
    private void RestoreParameterInheritance()
    {
        if (SelectedNode is not { IsLeaf: true } node) return;
        ApplyMutation(() => node.Model.Parameters = new JObject(), node);
    }

    /// <summary>读取当前节点有效参数的独立副本，卡片草稿不会受开关刷新影响。</summary>
    internal JObject GetOneDragonParameters(PuloniaTaskNodeViewModel node)
        => ResolveEffectiveParameters(node.Model, GetAncestorModels(node.Parent), out _);

    /// <summary>完整校验后一次提交卡片中真实改变的字段，其余字段继续继承。</summary>
    internal void ApplyOneDragonParameters(PuloniaTaskNodeViewModel node, JObject changes)
    {
        if (!EnumerateNodes().Contains(node) || IsDeleted)
            throw new InvalidOperationException("当前任务已移除，请重新选择。");
        if (changes.Count == 0) return;
        var effective = GetOneDragonParameters(node);
        foreach (var field in changes.Properties()) effective[field.Name] = field.Value.DeepClone();
        if (GetDefinition(node.Model) is { } definition)
            PuloniaTaskValidator.ValidateParameters(effective, definition.ParameterSchema, node.Name);
        ApplyMutation(() =>
        {
            // 同一变更包含所有卡片设置，撤销不会只恢复一部分参数。
            foreach (var field in changes.Properties()) node.Model.Parameters[field.Name] = field.Value.DeepClone();
        }, node);
    }
}
