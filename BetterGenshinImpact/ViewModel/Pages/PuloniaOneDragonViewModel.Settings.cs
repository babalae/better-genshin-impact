using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 一条龙右侧设置区：专属视图和通用卡片的每次输入都立即写入计划节点，由文档负责撤销和自动保存。
/// 连续修改同一组字段只占用一次撤销；无效输入不生效，保留上次生效的值并在设置区提示。
/// </summary>
public partial class PuloniaOneDragonViewModel
{
    /// <summary>右侧当前任务的老样式设置卡片。</summary>
    [ObservableProperty] private IReadOnlyList<PuloniaOneDragonParameterFieldViewModel> _settingsFields = [];
    /// <summary>右侧当前任务的专属设置视图；为空时使用通用参数卡片。</summary>
    [ObservableProperty] private SoloTaskSettingsViewModel? _fixedSettings;
    /// <summary>最近一次即时提交的问题提示；全部生效时为空。</summary>
    [ObservableProperty] private string _settingsMessage = "";

    /// <summary>当前设置区所属的清单节点。</summary>
    private PuloniaTaskNodeViewModel? _settingsNode;
    /// <summary>建立设置区时节点的显式参数；未修改的字段据此恢复原来的显式值或继承状态。</summary>
    private JObject? _settingsBaseline;
    /// <summary>建立设置区或最近一次提交后的参数签名（显式参数与有效参数），用于识别其他入口改写了参数。</summary>
    private string? _settingsSignature;
    /// <summary>设置区版本；重建后旧视图模型的迟到事件不能再提交到当前节点。</summary>
    private int _settingsGeneration;
    /// <summary>正在提交设置；期间文档的内容变化事件不触发重建，避免打断输入焦点。</summary>
    private bool _isApplyingSettings;

    /// <summary>按节点当前有效参数重建设置区：固定任务使用专属视图，其余参数走通用卡片。</summary>
    /// <param name="node">要展示设置的清单节点。</param>
    private void RefreshSettingsSurfaces(PuloniaTaskNodeViewModel node)
    {
        // 1. 先让旧视图模型失效，再按最新有效参数创建新的输入
        var generation = ++_settingsGeneration;
        var values = node.Document.GetOneDragonParameters(node);
        var fixedSettings = SoloTaskSettingsFactory.Create(_registry, PuloniaSoloTaskExecutor.ToTaskId(node.TaskType), values);
        var definition = _service.Definitions.FirstOrDefault(d => d.TaskType == node.TaskType && d.ResourceId == node.Path)
                         ?? _service.Definitions.FirstOrDefault(d => d.TaskType == node.TaskType && d.ResourceId is null);
        var fields = PuloniaTaskSettingsEditorViewModel.CreateFields(definition, values);
        if (fixedSettings is not null)
        {
            // 专属界面已提供的参数不再重复显示为卡片
            var edited = fixedSettings.EditedParameterNames.ToHashSet(StringComparer.Ordinal);
            fields = fields.Where(field => !edited.Contains(field.Name)).ToArray();
        }

        // 2. 记录基线后再切换视图，提交时据此判断字段是否被修改
        _settingsNode = node;
        _settingsBaseline = (JObject)node.Model.Parameters.DeepClone();
        _settingsSignature = BuildSettingsSignature(node);
        FixedSettings = fixedSettings;
        SettingsFields = fields;

        // 3. 订阅输入变化；事件携带建立时的版本，过期视图的回写直接忽略
        if (fixedSettings is not null)
            fixedSettings.Edited += (_, _) => OnSettingsEdited(generation);
        foreach (var field in fields)
            TrackSettingsField(field, generation);
        if (fixedSettings is AutoBossSettingsViewModel boss)
            _ = boss.SetProgressScopeAsync(node.Document.Id, node.Id);
    }

    /// <summary>清空设置区，未选择任务时不保留任何输入。</summary>
    private void ClearSettingsSurfaces()
    {
        _settingsGeneration++;
        _settingsNode = null;
        _settingsBaseline = null;
        _settingsSignature = null;
        FixedSettings = null;
        SettingsFields = [];
    }

    /// <summary>递归订阅通用卡片及其子字段的输入变化。</summary>
    /// <param name="field">设置字段。</param>
    /// <param name="generation">建立设置区时的版本。</param>
    private void TrackSettingsField(PuloniaOneDragonParameterFieldViewModel field, int generation)
    {
        field.PropertyChanged += (_, _) => OnSettingsEdited(generation);
        foreach (var child in field.Children)
            TrackSettingsField(child, generation);
    }

    /// <summary>只有当前版本的设置区输入才会提交；提交引起的通知不重复进入。</summary>
    /// <param name="generation">事件来源设置区的版本。</param>
    private void OnSettingsEdited(int generation)
    {
        if (generation != _settingsGeneration || _isApplyingSettings) return;
        ApplySettingsEdits();
    }

    /// <summary>
    /// 把设置区输入换算为节点的显式参数并立即提交。
    /// 受控字段（专属视图字段与通用卡片字段）中：已修改的写入新值，未修改的恢复建立设置区时的显式值或继承状态，
    /// 输入无效的保留上次生效的值；其他显式参数原样保留。
    /// </summary>
    private void ApplySettingsEdits()
    {
        if (_settingsNode is not { } node || _settingsBaseline is not { } baseline) return;
        var changes = new JObject();
        var controlled = new HashSet<string>(StringComparer.Ordinal);
        var skipped = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();

        // 1. 专属视图按载入快照收集真实变化
        if (FixedSettings is { } fixedSettings)
        {
            controlled.UnionWith(fixedSettings.EditedParameterNames);
            var fixedChanges = new JObject();
            try
            {
                fixedSettings.CollectChanges(fixedChanges);
                foreach (var property in fixedChanges.Properties())
                {
                    controlled.Add(property.Name);
                    changes[property.Name] = property.Value;
                }
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or JsonException)
            {
                errors.Add(ex.Message);
                skipped.UnionWith(fixedSettings.EditedParameterNames);
            }
        }

        // 2. 通用卡片逐字段生成值，单个字段无效只跳过该字段
        foreach (var field in SettingsFields)
        {
            controlled.Add(field.Name);
            try
            {
                if (field.HasChanges() && field.BuildValue() is { } value)
                    changes[field.Name] = value;
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or JsonException)
            {
                errors.Add(ex.Message);
                skipped.Add(field.Name);
            }
        }

        // 3. 以节点当前显式参数为底，只改写受控字段
        var target = (JObject)node.Model.Parameters.DeepClone();
        foreach (var name in controlled)
        {
            if (skipped.Contains(name)) continue;
            if (changes[name] is { } value) target[name] = value.DeepClone();
            else if (baseline[name] is { } original) target[name] = original.DeepClone();
            else target.Remove(name);
        }

        // 4. 提交并刷新签名；Schema 校验失败时整次不生效
        _isApplyingSettings = true;
        try
        {
            if (node.Document.ReplaceOneDragonParameters(node, target))
                _settingsSignature = BuildSettingsSignature(node);
            SettingsMessage = errors.Count == 0 ? "" : "以下输入未生效，仍使用上次的值：" + string.Join("；", errors);
        }
        catch (Exception ex) when (ex is PuloniaTaskValidationException or InvalidOperationException)
        {
            SettingsMessage = "设置未生效：" + ex.Message;
        }
        finally { _isApplyingSettings = false; }
    }

    /// <summary>高级设置、公共设置、预设或撤销改写了当前节点参数时，按最新值重建设置区。</summary>
    private void OnConfigurationContentChanged(object? sender, EventArgs e)
    {
        if (_isApplyingSettings || _settingsNode is not { } node || !ReferenceEquals(sender, node.Document)
            || !TaskList.Contains(node)) return;
        // 节点包装被撤销或重新加载替换时，由选择变化负责重建
        if (BuildSettingsSignature(node) == _settingsSignature) return;
        RefreshSettingsSurfaces(node);
        SettingsMessage = "";
    }

    /// <summary>生成显式参数与有效参数的组合签名；任一变化都说明设置区的基线已经过期。</summary>
    /// <param name="node">清单节点。</param>
    /// <returns>参数签名。</returns>
    private static string BuildSettingsSignature(PuloniaTaskNodeViewModel node)
        => node.Model.Parameters.ToString(Formatting.None) + "\n"
           + node.Document.GetOneDragonParameters(node).ToString(Formatting.None);
}
