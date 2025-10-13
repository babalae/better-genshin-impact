using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;
using System.Windows;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls.Primitives;

namespace BetterGenshinImpact.View.Pages;

public partial class IntegratedRecordPage : UserControl
{
    private readonly IntegratedRecordPageViewModel _viewModel;

    public IntegratedRecordPage(IntegratedRecordPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        
        // 注册卸载事件
        this.Unloaded += OnUnloaded;
    }



    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // 清理资源
        _viewModel?.Cleanup();
    }
}