using BetterGenshinImpact.ViewModel.Controls;
using System.Windows.Controls;
using System.Windows;

namespace BetterGenshinImpact.View.Controls;

/// <summary>用于开发中页签和无可显示内容状态的狛荷屋占位画面。</summary>
public partial class AccountRecordsEmptyState : UserControl
{
    /// <summary>加载状态复用水印，并替换随机占位文字。</summary>
    public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.Register(
        nameof(IsLoading), typeof(bool), typeof(AccountRecordsEmptyState), new PropertyMetadata(false));
    /// <summary>是否显示加载提示。</summary>
    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }
    /// <summary>独立管理占位语句，不覆盖所在页面的数据上下文。</summary>
    public AccountRecordsEmptyStateViewModel ViewModel { get; } = new();

    /// <summary>初始化可复用的水印和提示文字。</summary>
    public AccountRecordsEmptyState()
    {
        InitializeComponent();
    }
}
