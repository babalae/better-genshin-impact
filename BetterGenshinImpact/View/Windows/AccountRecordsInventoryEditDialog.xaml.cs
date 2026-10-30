using System;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Controls;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>库存编辑窗口，只负责窗口生命周期。</summary>
public partial class AccountRecordsInventoryEditDialog : FluentWindow
{
    /// <summary>本次编辑视图模型。</summary>
    private readonly AccountRecordsInventoryEditViewModel _viewModel;
    /// <summary>初始化主题并绑定保存完成事件。</summary>
    public AccountRecordsInventoryEditDialog(AccountRecordsInventoryEditViewModel viewModel)
    {
        DataContext = _viewModel = viewModel;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
        _viewModel.Saved += OnSaved;
        Closed += OnClosed;
    }
    /// <summary>保存成功后关闭模态窗口。</summary>
    private void OnSaved() { if (IsVisible) DialogResult = true; }
    /// <summary>释放事件订阅，防止窗口留在视图模型上。</summary>
    private void OnClosed(object? sender, EventArgs e) => _viewModel.Saved -= OnSaved;
}
