using System;
using System.Windows;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
using Newtonsoft.Json.Linq;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>体力计划子任务设置窗口外框，只负责主题与模态结果；字段与校验由视图模型提供。</summary>
public partial class ResinPlanSubTaskSettingsWindow : FluentWindow
{
    /// <summary>窗口的设置草稿。</summary>
    private readonly ResinPlanSubTaskSettingsViewModel _viewModel;

    /// <summary>确认后的完整参数；取消时为 null。</summary>
    public JObject? Result { get; private set; }

    /// <summary>注入设置草稿，标题、说明和卡片使用同一 DataContext。</summary>
    /// <param name="viewModel">设置草稿。</param>
    public ResinPlanSubTaskSettingsWindow(ResinPlanSubTaskSettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>跟随应用主题启用系统背景。</summary>
    private void OnSourceInitialized(object? sender, EventArgs e) => WindowHelper.TryApplySystemBackdrop(this);

    /// <summary>输入通过校验后才关闭并返回确定。</summary>
    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.TryBuild() is { } result)
        {
            Result = result;
            DialogResult = true;
        }
    }

    /// <summary>取消或 Esc 丢弃整个草稿。</summary>
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
