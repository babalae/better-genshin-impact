using System;
using System.Windows;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Resin;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>树脂设置窗口外框，只负责主题与模态结果。</summary>
public partial class ResinSettingsDialog : FluentWindow
{
    /// <summary>注入编辑器，标题、摘要、校验和内容模板均使用同一 DataContext。</summary>
    public ResinSettingsDialog(IResinSettingsDialogContent content)
    {
        DataContext = content;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>跟随应用主题启用系统背景。</summary>
    private void OnSourceInitialized(object? sender, EventArgs e) => WindowHelper.TryApplySystemBackdrop(this);

    /// <summary>仅在当前草稿有效且确认完成后返回确定。</summary>
    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is IResinSettingsDialogContent { CanConfirm: true }) DialogResult = true;
    }

    /// <summary>取消或 Esc 丢弃整个草稿。</summary>
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
