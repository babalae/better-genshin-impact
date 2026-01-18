using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows;
using System;
using System.Windows;

namespace BetterGenshinImpact.View.Windows;

public partial class FeedWindow
{
    public FeedWindowViewModel ViewModel { get; }

    public FeedWindow(FeedWindowViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
        
        this.Loaded += FeedWindow_Loaded;
        this.SourceInitialized += FeedWindow_SourceInitialized;
    }

    public FeedWindow() : this(new FeedWindowViewModel())
    {
    }

    private void FeedWindow_SourceInitialized(object? sender, EventArgs e)
    {
        // 应用与主窗口相同的背景主题
        WindowHelper.TryApplySystemBackdrop(this);
    }

    private void FeedWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 窗口加载完成后拉取远程兑换码数据
        _ = ViewModel.LoadRemoteDataAsync();
    }

    private void BtnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}