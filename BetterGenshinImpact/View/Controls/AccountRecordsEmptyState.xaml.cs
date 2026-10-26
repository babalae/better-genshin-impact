using BetterGenshinImpact.ViewModel.Controls;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Controls;

/// <summary>用于开发中页签和无可显示内容状态的狛荷屋占位画面。</summary>
public partial class AccountRecordsEmptyState : UserControl
{
    /// <summary>独立管理占位语句，不覆盖所在页面的数据上下文。</summary>
    public AccountRecordsEmptyStateViewModel ViewModel { get; } = new();

    /// <summary>初始化可复用的水印和提示文字。</summary>
    public AccountRecordsEmptyState()
    {
        InitializeComponent();
    }
}
