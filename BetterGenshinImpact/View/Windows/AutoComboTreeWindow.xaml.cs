using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.DpiAwareness;
using BetterGenshinImpact.ViewModel.Windows;
using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Vanara.PInvoke;

namespace BetterGenshinImpact.View.Windows;

public partial class AutoComboTreeWindow : Window
{
    /// <summary>浮窗左边界距游戏画面左边缘的内边距（1080p 基准 DIP，按分辨率等比缩放）</summary>
    private const double LeftMarginSize = 20;

    /// <summary>浮窗顶边距游戏画面上边缘的内边距（1080p 基准 DIP，按分辨率等比缩放）；默认落位约在日志遮罩上方</summary>
    private const double TopMarginSize = 240;

    public AutoComboTreeWindow()
    {
        InitializeComponent();
        DataContext = AutoComboTreeViewModel.Instance;
        this.InitializeDpiAwareness();
        ShowActivated = false;
        Opacity = 0;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    /// <summary>
    /// 每次 Show 时按游戏窗口当前位置重新定位（窗口化运行时游戏窗口位置可能变化）
    /// 定位前校验截图器已初始化，否则 SystemInfo/CaptureAreaRect 为 null；
    /// 且需延迟到 Dispatcher 加载优先级执行——IsVisibleChanged 触发时 WPF 初始定位流程尚未完成，直接赋值 Left/Top 会被覆盖
    /// </summary>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
        {
            return;
        }

        // 每次 Show 都重新定位；此时截图器应已初始化（任务在 TaskRunner.Init 校验后才执行）
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, PositionToLogBox);
    }

    private void PositionToLogBox()
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            return;
        }

        PositionNearGame(TaskContext.Instance().SystemInfo.CaptureAreaRect);
        // 定位成功后淡入显示，保证窗口从可见那一刻起就在正确位置
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
    }

    /// <summary>
    /// 绝对定位（Left + Top 体系，与 PictureInPictureWindow.PositionNearGame 一致）：
    /// 捕获矩形（物理像素）按游戏所在显示器 DPI 缩放为 WPF DIP，再加左、上两个内边距（按 1080p 基准等比缩放）；
    /// 同时给根容器套 LayoutTransform，让窗口内容按 1080p 基准随游戏分辨率等比缩放（2K/4K 下浮窗相对游戏大小不变）
    /// </summary>
    private void PositionNearGame(RECT captureRect)
    {
        var dpi = DpiHelper.ScaleY;
        // 以 1080p 为基准的缩放系数：游戏窗口越宽，浮窗整体越大
        var scale = captureRect.Width / dpi / 1920.0;

        if (RootBorder.LayoutTransform is ScaleTransform st)
        {
            st.ScaleX = scale;
            st.ScaleY = scale;
        }
        else
        {
            RootBorder.LayoutTransform = new ScaleTransform(scale, scale);
        }

        // Window 的 Width/Height 需同步放大（LayoutTransform 只作用于 Border 内容，不影响窗口自身尺寸）
        Width = 465 * scale;
        Height = 550 * scale;

        Left = captureRect.Left / dpi + LeftMarginSize * scale;
        Top = captureRect.Top / dpi + TopMarginSize * scale;
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = User32.GetWindowLong(hwnd, User32.WindowLongFlags.GWL_EXSTYLE);
        _ = User32.SetWindowLong(hwnd, User32.WindowLongFlags.GWL_EXSTYLE, exStyle | (int)User32.WindowStylesEx.WS_EX_TOOLWINDOW);

        // 句柄已建、尚未显示：此时赋 Left/Top 会随 CreateWindow 生效，是 Manual 定位的规范时机
        PositionToLogBox();
    }

    protected override void OnClosed(EventArgs e)
    {
        IsVisibleChanged -= OnIsVisibleChanged;
        base.OnClosed(e);
    }
}
