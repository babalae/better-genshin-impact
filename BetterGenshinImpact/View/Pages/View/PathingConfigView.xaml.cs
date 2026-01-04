using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Pages.View;

namespace BetterGenshinImpact.View.Pages.View;

/// <summary>
/// PathingConfigView.xaml 的交互逻辑
/// </summary>
public partial class PathingConfigView
{
    private PathingConfigViewModel ViewModel { get; }

    public PathingConfigView(PathingConfigViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
        SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(this);
    }
}
