using System.Linq;
using System.Windows;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Controls;
using BetterGenshinImpact.View.Pages.View;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using BetterGenshinImpact.ViewModel.Pages.View;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.View.Windows.Pulonia;

/// <summary>
/// 高级设置窗口使用的公共设置与采集 CD 表单；在视图层创建窗口并读取软件配置，结果交回视图模型提交。
/// </summary>
public sealed class PuloniaTaskSettingsDialogs : IPuloniaTaskSettingsDialogs
{
    /// <summary>软件配置；配置组表单需要读取全局配置中的队伍与战斗选项。</summary>
    private readonly IConfigService _configService;

    /// <summary>注入软件配置服务。</summary>
    /// <param name="configService">配置服务。</param>
    public PuloniaTaskSettingsDialogs(IConfigService configService) => _configService = configService;

    /// <inheritdoc />
    public PuloniaTaskSettingsDialogResult? EditCommonSettings(string taskType, bool isGroup, JObject effective)
    {
        // 在独立副本上编辑，确认时由调用方只提交变化字段；取消不会污染计划或全局配置。
        var config = PuloniaTaskCommonSettings.FromParameters(taskType, effective);
        var title = taskType switch
        {
            "pathing" => "地图追踪设置",
            "javascript" => "JS 行走、战斗与食物设置",
            _ => "Shell 执行设置"
        };
        var viewModel = new ScriptGroupConfigViewModel(_configService.Get(), config)
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
        dialog = new PromptDialog(isGroup
                ? "修改的字段应用于本组下的同类任务；子组、资源专用设置和任务自身设置优先。"
                : "显示当前有效设置；只将修改的字段保存为本任务的设置。",
            title, new ScriptGroupConfigView(viewModel), null, dialogConfig)
        {
            Owner = GetOwner(),
            Width = 850,
            Height = 720,
            MinWidth = 850,
            MinHeight = 520,
            SizeToContent = SizeToContent.Manual,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        if (dialog.ShowDialog() != true)
            return null;
        return new PuloniaTaskSettingsDialogResult(PuloniaTaskCommonSettings.ToParameters(taskType, config), restoreInheritance);
    }

    /// <inheritdoc />
    public PuloniaTaskSettingsDialogResult? EditCollectionCooldown(PuloniaCollectionCooldownViewModel draft, bool isLeaf)
    {
        var restore = false;
        PromptDialog? dialog = null;
        var config = new PromptDialogConfig
        {
            ShowLeftButton = true,
            LeftButtonText = "恢复继承",
            LeftButtonClick = (_, _) => { restore = true; dialog!.DialogResult = true; }
        };
        dialog = new PromptDialog(isLeaf ? "仅完整执行成功后记录采集时间；修改规则会重算已有 CD。"
                : "此配置由下级地图追踪任务整体继承，子任务可单独覆盖。",
            "采集 CD", new PuloniaCollectionCooldownView { DataContext = draft }, null, config)
        {
            Owner = GetOwner(),
            Width = 560, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        return dialog.ShowDialog() == true ? new PuloniaTaskSettingsDialogResult(null, restore) : null;
    }

    /// <summary>以当前活动窗口为所有者，使表单显示在打开它的高级设置窗口之上。</summary>
    private static Window GetOwner()
        => Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
           ?? Application.Current.MainWindow;
}
