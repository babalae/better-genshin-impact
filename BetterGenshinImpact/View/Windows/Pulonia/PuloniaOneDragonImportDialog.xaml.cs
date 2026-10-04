using System.Windows;
using BetterGenshinImpact.Pulonia.Models;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows.Pulonia;

/// <summary>显示完整导入预览及兼容差异，用户确认前不写入计划。</summary>
public partial class PuloniaOneDragonImportDialog : FluentWindow
{
    /// <summary>本次生成的独立预览。</summary>
    public PuloniaTaskImportResult Result { get; }
    /// <summary>绑定预览结果并建立窗口。</summary>
    public PuloniaOneDragonImportDialog(PuloniaTaskImportResult result)
    { Result = result; DataContext = Result; InitializeComponent(); }
    /// <summary>确认明确展示的数据，领域转换和持久化由界面包装服务处理。</summary>
    private void ConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
