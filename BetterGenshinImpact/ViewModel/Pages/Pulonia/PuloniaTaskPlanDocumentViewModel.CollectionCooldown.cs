using System;
using System.Linq;
using System.Windows;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Controls;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>地图追踪 CD 草稿编辑与整对象继承，复用文档撤销和自动保存边界。</summary>
public partial class PuloniaTaskPlanDocumentViewModel
{
    /// <summary>选中地图追踪叶子或分组时提供采集 CD 设置。</summary>
    public bool CanEditCollectionCooldown => SelectedNode?.TaskType is "pathing" or "group";

    /// <summary>当前材料、规则和配置来源的简要预览。</summary>
    public string CollectionCooldownSummary
    {
        get
        {
            if (!CanEditCollectionCooldown) return string.Empty;
            try
            {
                var draft = CreateCollectionCooldownDraft(SelectedNode!);
                return draft.Preview + "\n配置来源：" + draft.ConfigurationSource;
            }
            catch (Exception ex) when (ex is PuloniaTaskValidationException or ArgumentException) { return "配置无效：" + ex.Message; }
        }
    }

    /// <summary>从有效参数创建独立草稿，目录引用组预览只解释该组规则。</summary>
    internal PuloniaCollectionCooldownViewModel CreateCollectionCooldownDraft(PuloniaTaskNodeViewModel node)
    {
        var parameters = node.IsLeaf
            ? ResolveEffectiveParameters(node.Model, GetAncestorModels(node.Parent), out _)
            : GetCommonSettingsParameters(node, "pathing");
        var source = "分组继承设置";
        if (node.IsLeaf)
        {
            ResolveEffectiveParameters(node.Model, GetAncestorModels(node.Parent), out var sources);
            source = sources.TryGetValue(PuloniaCollectionCooldown.ParameterName, out var origin) ? origin : "能力默认值";
        }
        var draft = new PuloniaCollectionCooldownViewModel();
        draft.Load(parameters[PuloniaCollectionCooldown.ParameterName], source);
        draft.SetResource(node.Model.Path ?? node.Model.Source?.Path,
            node.Model.Resource ?? node.Model.Source?.Resource, node.Model.Source?.Kind == "directory");
        return draft;
    }

    /// <summary>在模态草稿表单中编辑；取消不写入，确认和恢复继承均作为一次文档变更。</summary>
    [RelayCommand]
    private void EditCollectionCooldown()
    {
        var node = SelectedNode;
        if (node is null || !CanEditCollectionCooldown) return;
        try
        {
            var draft = CreateCollectionCooldownDraft(node);
            var original = draft.BuildValue();
            var restore = false;
            PromptDialog? dialog = null;
            var config = new PromptDialogConfig
            {
                ShowLeftButton = true,
                LeftButtonText = "恢复继承",
                LeftButtonClick = (_, _) => { restore = true; dialog!.DialogResult = true; }
            };
            dialog = new PromptDialog(node.IsLeaf ? "仅完整执行成功后记录采集时间；修改规则会重算已有 CD。"
                : "此配置由下级地图追踪任务整体继承，子任务可单独覆盖。",
                "采集 CD", new PuloniaCollectionCooldownView { DataContext = draft }, null, config)
            {
                Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive) ?? Application.Current.MainWindow,
                Width = 560, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            if (dialog.ShowDialog() != true) return;
            ApplyCollectionCooldown(node, original, restore ? null : draft.BuildValue(), restore);
        }
        catch (Exception ex) when (ex is PuloniaTaskValidationException or FormatException or ArgumentException or OverflowException)
        { ThemedMessageBox.Error("采集 CD 设置未应用：" + ex.Message, "采集 CD"); }
    }

    /// <summary>只更新采集 CD 参数对象，不改变其他设置；整次变更支持撤销。</summary>
    internal void ApplyCollectionCooldown(PuloniaTaskNodeViewModel node, JObject original, JObject? edited, bool restore)
    {
        if (!restore && JToken.DeepEquals(original, edited)) return;
        if (!restore) PuloniaCollectionCooldown.ValidateSettings(edited, "采集 CD");
        ApplyMutation(() =>
        {
            if (node.IsLeaf)
            {
                if (restore) node.Model.Parameters.Remove(PuloniaCollectionCooldown.ParameterName);
                else node.Model.Parameters[PuloniaCollectionCooldown.ParameterName] = edited!.DeepClone();
                return;
            }
            var scope = node.Model.ParameterOverrides.FirstOrDefault(item => item.TaskType == "pathing" && item.ResourceId is null && item.SchemaVersion == 1);
            if (restore)
            {
                scope?.Values.Remove(PuloniaCollectionCooldown.ParameterName);
                if (scope?.Values.Count == 0) node.Model.ParameterOverrides.Remove(scope);
                return;
            }
            if (scope is null)
            {
                scope = new PuloniaTaskParameterOverride { TaskType = "pathing" };
                node.Model.ParameterOverrides.Add(scope);
            }
            scope.Values[PuloniaCollectionCooldown.ParameterName] = edited!.DeepClone();
        }, node);
        EditorMessage = restore ? "已恢复继承的采集 CD。" : "采集 CD 已应用，将自动保存。";
    }
}
