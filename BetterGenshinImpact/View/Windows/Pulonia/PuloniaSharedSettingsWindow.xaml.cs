using System.Windows;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows.Pulonia;

/// <summary>
/// 节点共享设置窗口；编辑器的释放由打开它的入口负责。
/// </summary>
public partial class PuloniaSharedSettingsWindow : FluentWindow
{
    /// <summary>使用共享设置编辑器建立窗口。</summary>
    /// <param name="editor">节点模式的编辑器。</param>
    public PuloniaSharedSettingsWindow(PuloniaSharedSettingsEditorViewModel editor)
    {
        DataContext = editor;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
    }

    /// <summary>修改已即时生效，关闭只结束编辑。</summary>
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
