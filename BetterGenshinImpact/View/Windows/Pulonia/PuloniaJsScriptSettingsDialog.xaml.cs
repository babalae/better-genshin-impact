using System;
using System.Windows;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Newtonsoft.Json.Linq;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows.Pulonia;

/// <summary>
/// JS 脚本自定义设置弹窗；新增 JS 任务后与编辑配置时按 settings_ui 展示表单，确认前不修改任务计划。
/// </summary>
public partial class PuloniaJsScriptSettingsDialog : FluentWindow
{
    /// <summary>弹窗使用的视图模型。</summary>
    public PuloniaJsScriptSettingsDialogViewModel ViewModel { get; }

    /// <summary>
    /// 使用已经准备好设置字段的视图模型建立弹窗。
    /// </summary>
    /// <param name="viewModel">脚本自定义设置视图模型。</param>
    public PuloniaJsScriptSettingsDialog(PuloniaJsScriptSettingsDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
        ViewModel.RequestClose += OnRequestClose;
        Closed += OnClosed;
    }

    /// <summary>
    /// 以模态方式显示弹窗，确认后返回完整的 settings 对象。
    /// </summary>
    /// <param name="viewModel">脚本自定义设置视图模型。</param>
    /// <param name="owner">所属窗口；为空时使用主窗口。</param>
    /// <returns>确认的 settings；取消时为 null。</returns>
    public static JObject? Show(PuloniaJsScriptSettingsDialogViewModel viewModel, Window? owner = null)
    {
        var dialog = new PuloniaJsScriptSettingsDialog(viewModel)
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        return dialog.ShowDialog() == true ? viewModel.Result : null;
    }

    /// <summary>响应视图模型的确认或取消请求。</summary>
    private void OnRequestClose(object? sender, bool result) => DialogResult = result;

    /// <summary>窗口关闭时解除事件订阅。</summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        ViewModel.RequestClose -= OnRequestClose;
        Closed -= OnClosed;
    }
}
