using System;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Core.Script.Settings;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.View.Windows.Pulonia;

/// <summary>
/// 新建 JS 任务后、插入任务树前编辑脚本设置：声明了 HTML 设置页时打开设置页面，否则按 settings_ui 打开基础表单。
/// 任务计划和新一条龙的添加流程共用。
/// </summary>
public static class PuloniaJavaScriptTaskCreationSettings
{
    /// <summary>提示框标题。</summary>
    private const string DialogTitle = "脚本自定义设置";

    /// <summary>
    /// 编辑新任务的脚本设置，结果写入任务参数 settings。
    /// </summary>
    /// <param name="creation">创建弹窗的结果。</param>
    /// <param name="resourceBaseDirectory">本地相对路径的能力资源根目录。</param>
    /// <param name="basicFormEdited">创建弹窗是否已内嵌基础表单（新一条龙）；为 true 时没有 HTML 设置页就不再弹出基础表单。</param>
    /// <param name="owner">所属窗口。</param>
    /// <returns>是否继续添加任务；用户取消时为 false。</returns>
    public static async Task<bool> EditAsync(PuloniaTaskCreationResult creation, string? resourceBaseDirectory,
        bool basicFormEdited, Window owner)
    {
        var task = creation.Task;
        var items = creation.JavaScriptSettingItems;
        if (creation.HasSettingsHtml)
        {
            // 1. HTML 设置页：页面每次保存只写入草稿，关闭窗口后用最后一次保存的值添加任务
            JObject? saved = null;
            string? reason;
            try
            {
                using var preview = await PuloniaTaskResourcePreviewLoader.LoadJavaScriptTaskAsync(task,
                    resourceBaseDirectory, includeSettingsPage: true);
                reason = preview.SettingsPage is { } page
                    ? JsScriptHtmlSettingsWindow.Show(new ScriptSettingsEditRequest(page, "pulonia", true,
                        () => saved ?? task.Parameters["settings"] as JObject ?? new JObject(),
                        settings => saved = settings), owner)
                    : preview.SettingsPageError;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
            }

            if (reason is null)
            {
                if (saved is null)
                    return ThemedMessageBox.Question("设置页没有保存设置，仍然添加任务吗？", DialogTitle,
                        MessageBoxButton.YesNo) == MessageBoxResult.Yes;
                SetSettings(task, saved);
                return true;
            }

            // 2. 设置页打不开：没有基础表单时由用户决定是否添加，否则改用基础表单
            if (items.Count == 0)
                return ThemedMessageBox.Warning($"无法打开脚本设置页面：{reason}\n仍然添加任务吗？", DialogTitle,
                    MessageBoxButton.YesNo) == MessageBoxResult.Yes;
            ThemedMessageBox.Warning($"无法打开脚本设置页面：{reason}\n将改用基础设置表单。", DialogTitle);
        }
        else if (basicFormEdited || items.Count == 0)
        {
            return true;
        }

        // 3. 基础表单：取消则不添加
        var viewModel = new PuloniaJsScriptSettingsDialogViewModel(task.Name, items,
            task.Parameters["settings"] as JObject, isCreation: true);
        if (PuloniaJsScriptSettingsDialog.Show(viewModel, owner) is not { } basicSettings)
            return false;
        SetSettings(task, basicSettings);
        return true;
    }

    /// <summary>
    /// 写入任务参数 settings；空对象时移除参数，与基础表单一致。
    /// </summary>
    private static void SetSettings(PuloniaTask task, JObject settings)
    {
        if (settings.Count > 0)
            task.Parameters["settings"] = settings;
        else
            task.Parameters.Remove("settings");
    }
}
