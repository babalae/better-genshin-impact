using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.DpiAwareness;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel;
using Microsoft.Extensions.Logging;
using Serilog.Sinks.RichTextBox.Abstraction;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using Vanara.PInvoke;

namespace BetterGenshinImpact.View;

/// <summary>
/// 覆盖在游戏窗口上的遮罩窗口，用于显示识别结果、日志、状态、指标、地图点位等。
/// 只能由 IMaskWindowHost 通过 DI 创建；View 层以外的代码不应引用本类型。
/// </summary>
public partial class MaskWindow : Window
{
    private readonly MaskWindowViewModel _viewModel;
    private readonly IRichTextBox _richTextBox;
    private readonly ILogger<MaskWindow> _logger;

    private MapLabelSearchWindow? _mapLabelSearchWindow;
    private CancellationTokenSource? _mapLabelCategorySelectCts;

    static MaskWindow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(MaskWindow), new FrameworkPropertyMetadata(typeof(MaskWindow)));
    }

    public MaskWindow(MaskWindowViewModel viewModel, IRichTextBox richTextBox, ILogger<MaskWindow> logger)
    {
        _viewModel = viewModel;
        _richTextBox = richTextBox;
        _logger = logger;
        DataContext = viewModel;

        this.SetResourceReference(StyleProperty, typeof(MaskWindow));
        InitializeComponent();
        this.InitializeDpiAwareness();

        LogTextBox.TextChanged += LogTextBoxTextChanged;
        Loaded += OnLoaded;
        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _richTextBox.RichTextBox = LogTextBox;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        // 先设置窗口样式，再触发 SourceInitialized，保证 WindowClickThroughBehavior 最后应用点击穿透状态
        this.SetLayeredWindow();
        this.SetChildWindow();
        this.HideFromAltTab();
        base.OnSourceInitialized(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        LogTextBox.TextChanged -= LogTextBoxTextChanged;

        _mapLabelCategorySelectCts?.Cancel();
        _mapLabelCategorySelectCts?.Dispose();
        _mapLabelCategorySelectCts = null;

        if (_mapLabelSearchWindow != null)
        {
            _mapLabelSearchWindow.Close();
            _mapLabelSearchWindow = null;
        }

        base.OnClosed(e);
    }

    /// <summary>
    /// 点位选择器关闭时，同步隐藏它弹出的搜索窗口（搜索窗口是本视图自己持有的子窗口）
    /// </summary>
    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MaskWindowViewModel.IsMapPointPickerOpen)
            && !_viewModel.IsMapPointPickerOpen
            && _mapLabelSearchWindow != null)
        {
            _ = Dispatcher.InvokeAsync(() => _mapLabelSearchWindow?.Hide());
        }
    }

    private void LogTextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (LogTextBox.Document.Blocks.FirstBlock is Paragraph p && p.Inlines.Count > 200)
        {
            (p.Inlines as System.Collections.IList).RemoveAt(0);
        }

        var textRange = new TextRange(LogTextBox.Document.ContentStart, LogTextBox.Document.ContentEnd);
        if (textRange.Text.Length > 10000)
        {
            LogTextBox.Document.Blocks.Clear();
        }

        LogTextBox.ScrollToEnd();
    }

    private void MapLabelSearchTextBox_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_mapLabelSearchWindow == null)
        {
            _mapLabelSearchWindow = new MapLabelSearchWindow();
            _mapLabelSearchWindow.AttachViewModel(_viewModel);
        }

        var textbox = (FrameworkElement)sender;
        var point = textbox.PointToScreen(new Point(0, 0));
        var popupHeight = _mapLabelSearchWindow.ActualHeight > 0 ? _mapLabelSearchWindow.ActualHeight : _mapLabelSearchWindow.Height;

        _mapLabelSearchWindow.Left = point.X / DpiHelper.ScaleY;
        _mapLabelSearchWindow.Top = (point.Y - 4) / DpiHelper.ScaleY - popupHeight;

        if (!_mapLabelSearchWindow.IsVisible)
        {
            _mapLabelSearchWindow.Show();
        }

        _mapLabelSearchWindow.Topmost = true;
        _mapLabelSearchWindow.FocusSearch();

        e.Handled = true;
    }

    private void MapLabelCategoriesListView_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var container = ItemsControl.ContainerFromElement(MapLabelCategoriesListView, e.OriginalSource as DependencyObject) as ListViewItem;
        if (container == null)
        {
            return;
        }

        var item = MapLabelCategoriesListView.ItemContainerGenerator.ItemFromContainer(container) as MapLabelCategoryVm;
        if (item == null)
        {
            return;
        }

        if (ReferenceEquals(MapLabelCategoriesListView.SelectedItem, item))
        {
            return;
        }

        MapLabelCategoriesListView.SelectedItem = item;

        _mapLabelCategorySelectCts?.Cancel();
        _mapLabelCategorySelectCts?.Dispose();
        _mapLabelCategorySelectCts = new CancellationTokenSource();
        _ = SelectMapLabelCategoryAsync(item, _mapLabelCategorySelectCts.Token);
    }

    private async Task SelectMapLabelCategoryAsync(MapLabelCategoryVm item, CancellationToken ct)
    {
        try
        {
            await _viewModel.SelectMapLabelCategoryCommand.ExecuteAsync(item);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "切换地图标点分类时发生异常");
        }
    }
}

file static class MaskWindowExtension
{
    public static void HideFromAltTab(this Window window)
    {
        var hWnd = new WindowInteropHelper(window).Handle;
        int style = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);
        style |= (int)User32.WindowStylesEx.WS_EX_TOOLWINDOW;
        User32.SetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE, style);
    }

    public static void SetLayeredWindow(this Window window)
    {
        var hWnd = new WindowInteropHelper(window).Handle;
        int style = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);
        style |= (int)User32.WindowStylesEx.WS_EX_TRANSPARENT;
        style |= (int)User32.WindowStylesEx.WS_EX_LAYERED;
        _ = User32.SetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE, style);
    }

    public static void SetChildWindow(this Window window)
    {
        var hWnd = new WindowInteropHelper(window).Handle;
        int style = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_STYLE);
        style |= (int)User32.WindowStyles.WS_CHILD;
        _ = User32.SetWindowLong(hWnd, User32.WindowLongFlags.GWL_STYLE, style);
    }
}
