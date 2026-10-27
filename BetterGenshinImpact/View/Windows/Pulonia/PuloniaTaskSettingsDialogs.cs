using System.Linq;
using System.Windows;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Controls;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.View.Windows.Pulonia;

/// <summary>
/// 共享设置、设置方案、采集 CD 与 JS 脚本设置窗口；在视图层创建窗口并读取软件配置，结果交回视图模型提交。
/// </summary>
public sealed class PuloniaTaskSettingsDialogs : IPuloniaTaskSettingsDialogs
{
    /// <summary>软件配置；“从全局配置创建方案”需要读取当前全局配置。</summary>
    private readonly IConfigService _configService;

    /// <summary>注入软件配置服务。</summary>
    /// <param name="configService">配置服务。</param>
    public PuloniaTaskSettingsDialogs(IConfigService configService) => _configService = configService;

    /// <inheritdoc />
    public void EditSharedSettings(PuloniaSharedSettingsEditorViewModel editor)
    {
        var window = new PuloniaSharedSettingsWindow(editor) { Owner = GetOwner() };
        try
        {
            window.ShowDialog();
        }
        finally
        {
            editor.Dispose();
        }
    }

    /// <inheritdoc />
    public void ManageProfiles(PuloniaPlanEditingSession session, string? selectProfileId)
    {
        // 全局配置只在用户点击“从全局配置创建”时读取一次并复制，之后修改全局配置不影响方案。
        var manager = new PuloniaSettingsProfileManagerViewModel(session, this,
            () => PuloniaLegacySettingsConverter.FromGlobalConfig(_configService.Get()), selectProfileId);
        var window = new PuloniaSettingsProfileManagerWindow(manager) { Owner = GetOwner() };
        try
        {
            window.ShowDialog();
        }
        finally
        {
            manager.Dispose();
        }
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

    /// <inheritdoc />
    public JObject? EditJavaScriptSettings(PuloniaJsScriptSettingsDialogViewModel viewModel)
        => PuloniaJsScriptSettingsDialog.Show(viewModel, GetOwner());

    /// <summary>以当前活动窗口为所有者，使窗口显示在打开它的窗口之上。</summary>
    private static Window GetOwner()
        => Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
           ?? Application.Current.MainWindow;
}
