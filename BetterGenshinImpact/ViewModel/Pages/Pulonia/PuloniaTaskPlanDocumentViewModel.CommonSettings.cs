using System;
using System.Linq;
using System.Windows;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Pages.View;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.View;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 复用配置组表单编辑节点或分组的类型公共参数，所有提交均纳入文档撤销与自动保存。
/// </summary>
public partial class PuloniaTaskPlanDocumentViewModel
{
    /// <summary>
    /// 当前节点是否能够配置地图追踪公共设置。
    /// </summary>
    public bool CanEditPathingSettings => SelectedNode?.TaskType is "group" or "pathing";

    /// <summary>
    /// 当前节点是否能够配置 JS 使用的行走、战斗和食物设置。
    /// </summary>
    public bool CanEditJavaScriptSettings => SelectedNode?.TaskType is "group" or "javascript";

    /// <summary>
    /// 当前节点是否能够配置 Shell 公共设置。
    /// </summary>
    public bool CanEditShellSettings => SelectedNode?.TaskType is "group" or "shell";

    /// <summary>
    /// 是否展示原配置组设置的可视化编辑入口。
    /// </summary>
    public bool CanEditCommonSettings => CanEditPathingSettings || CanEditJavaScriptSettings || CanEditShellSettings;

    /// <summary>
    /// 在独立副本上编辑，确认时只提交变化字段；取消不会污染计划或全局配置。
    /// </summary>
    [RelayCommand]
    private void EditCommonSettings(string taskType)
    {
        var node = SelectedNode;
        if (node is null || taskType is not ("pathing" or "javascript" or "shell")
            || node.TaskType != "group" && node.TaskType != taskType)
            return;
        try
        {
            var config = PuloniaTaskCommonSettings.FromParameters(taskType, GetCommonSettingsParameters(node, taskType));
            var original = PuloniaTaskCommonSettings.ToParameters(taskType, config);
            var title = taskType switch
            {
                "pathing" => "地图追踪设置",
                "javascript" => "JS 行走、战斗与食物设置",
                _ => "Shell 执行设置"
            };
            var viewModel = new ScriptGroupConfigViewModel(TaskContext.Instance().Config, config)
            {
                ShowPathingSettings = taskType != "shell",
                ShowShellSettings = taskType == "shell",
                ShowLegacySchedulingSettings = false,
                ShowTitleBar = false,
                IsPulonia = true
            };
            var restoreInheritance = false;
            PromptDialog? dialog = null;
            var dialogConfig = new PromptDialogConfig
            {
                ShowLeftButton = true,
                LeftButtonText = "恢复继承",
                LeftButtonClick = (_, _) =>
                {
                    restoreInheritance = true;
                    dialog!.DialogResult = true;
                }
            };
            dialog = new PromptDialog(node.TaskType == "group"
                    ? "修改的字段应用于本组下的同类任务；子组、资源专用设置和任务自身设置优先。"
                    : "显示当前有效设置；只将修改的字段保存为本节点覆盖。",
                title, new ScriptGroupConfigView(viewModel), null, dialogConfig)
            {
                Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
                    ?? Application.Current.MainWindow,
                Width = 850,
                Height = 720,
                MinWidth = 850,
                MinHeight = 520,
                SizeToContent = SizeToContent.Manual,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            if (dialog.ShowDialog() != true)
                return;

            var edited = PuloniaTaskCommonSettings.ToParameters(taskType, config);
            ApplyCommonSettings(node, taskType, original, edited, restoreInheritance);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or Newtonsoft.Json.JsonException
                                   or ArgumentException or InvalidOperationException or OverflowException)
        {
            ThemedMessageBox.Error("任务设置未应用：" + ex.Message, "任务设置");
        }
    }

    /// <summary>
    /// 取得表单有效值；分组只预览该类型的通用覆盖，不混入某个具体资源的配置。
    /// </summary>
    internal JObject GetCommonSettingsParameters(PuloniaTaskNodeViewModel node, string taskType)
    {
        if (node.IsLeaf)
            return ResolveEffectiveParameters(node.Model, GetAncestorModels(node.Parent), out _);
        // 引用节点显式公共设置独立于外部祖先；与运行构建的引用边界保持一致。
        var ancestors = node.Model.Source?.Kind == "plan" ? [node.Model] : GetAncestorModels(node);
        return ResolveEffectiveParameters(new PuloniaTask { TaskType = taskType }, ancestors, out _);
    }

    /// <summary>
    /// 将表单变更写入叶子参数或类型通用覆盖；恢复继承只移除当前作用域的公共设置。
    /// </summary>
    internal void ApplyCommonSettings(PuloniaTaskNodeViewModel node, string taskType, JObject original,
        JObject edited, bool restoreInheritance)
    {
        var changes = new JObject(edited.Properties().Where(property =>
                PuloniaTaskCommonSettings.IsCommonParameter(taskType, property.Name)
                && !JToken.DeepEquals(original[property.Name], property.Value))
            .Select(property => new JProperty(property.Name, property.Value.DeepClone())));
        if (!restoreInheritance && changes.Count == 0)
            return;

        ApplyMutation(() =>
        {
            if (node.IsLeaf)
            {
                UpdateValues(node.Model.Parameters);
                return;
            }

            var scopes = node.Model.ParameterOverrides.Where(item => item.TaskType == taskType
                && item.ResourceId is null && item.SchemaVersion == 1).ToArray();
            if (restoreInheritance)
            {
                foreach (var scope in scopes)
                {
                    UpdateValues(scope.Values);
                    if (scope.Values.Count == 0)
                        node.Model.ParameterOverrides.Remove(scope);
                }
                return;
            }

            var target = scopes.LastOrDefault();
            if (target is null)
            {
                target = new PuloniaTaskParameterOverride { TaskType = taskType };
                node.Model.ParameterOverrides.Add(target);
            }
            UpdateValues(target.Values);
        }, node);
        EditorMessage = restoreInheritance ? "已恢复继承的任务设置。" : "任务设置已应用，将自动保存。";

        // 只处理本表单拥有的键，保留脚本 settings、Shell 命令和其他扩展参数。
        void UpdateValues(JObject values)
        {
            if (restoreInheritance)
            {
                foreach (var property in values.Properties()
                             .Where(property => PuloniaTaskCommonSettings.IsCommonParameter(taskType, property.Name)).ToArray())
                    property.Remove();
            }
            else
            {
                foreach (var property in changes.Properties())
                    values[property.Name] = property.Value.DeepClone();
            }
        }
    }
}
