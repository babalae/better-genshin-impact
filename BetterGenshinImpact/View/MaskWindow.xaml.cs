using BetterGenshinImpact.GameTask.Runtime;
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
    /// <summary>窗口拥有合并调度的日志维护，避免TextChanged同步递归裁剪。</summary>
    private readonly OverlayLogDocument _logDocument;

    /// <summary>
    /// 游戏运行环境服务。计算 DPI 时读取 <see cref="GameRuntimeService.Current"/> 的窗口句柄，不缓存。
    /// 本窗口经 Func&lt;MaskWindow&gt; 延迟创建，注入它不会与调度器形成 DI 环
    /// </summary>
    private readonly GameRuntimeService _runtimeService;

    private MapLabelSearchWindow? _mapLabelSearchWindow;
    private CancellationTokenSource? _mapLabelCategorySelectCts;

    static MaskWindow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(MaskWindow), new FrameworkPropertyMetadata(typeof(MaskWindow)));
    }

    public MaskWindow(MaskWindowViewModel viewModel, IRichTextBox richTextBox, ILogger<MaskWindow> logger, GameRuntimeService runtimeService)
    {
        _viewModel = viewModel;
        _richTextBox = richTextBox;
        _logger = logger;
        _runtimeService = runtimeService;
        DataContext = viewModel;

        this.SetResourceReference(StyleProperty, typeof(MaskWindow));
        InitializeComponent();
        this.InitializeDpiAwareness();

        _logDocument = new OverlayLogDocument(LogTextBox);
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
        _logDocument.Dispose();
        // 已关闭窗口不再接收后台排队日志；不能清除另一新窗口的目标。
        if (ReferenceEquals(_richTextBox.RichTextBox, LogTextBox)) _richTextBox.RichTextBox = null!;

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

        var scaleY = DpiHelper.GetScaleY(_runtimeService.Current?.Window.Handle ?? 0);
        _mapLabelSearchWindow.Left = point.X / scaleY;
        _mapLabelSearchWindow.Top = (point.Y - 4) / scaleY - popupHeight;

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
