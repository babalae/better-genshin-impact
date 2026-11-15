using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BetterGenshinImpact.Model.MaskMap;
using BetterGenshinImpact.View.Controls;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>实际点位画布的屏幕圆形命中、重叠消歧和菜单命令回归，不启动主程序或显示窗口。</summary>
public sealed class PointsCanvasTests
{
    /// <summary>用实际离屏像素验证隐藏图标不会盖住可见图标，命中和候选列表同步保持可见点优先。</summary>
    [Fact]
    public Task Stacking_HiddenPointStaysBelowVisiblePointAfterVisibilityChanges() => RunSta(canvas =>
    {
        var visible = PointAt("visible", 100, 100);
        visible.LabelId = "red";
        var hidden = PointAt("hidden", 100, 100);
        hidden.LabelId = "blue";
        hidden.IsHidden = true;
        canvas.LabelsSource =
        [
            new MaskMapPointLabel { LabelId = "red", Color = System.Drawing.Color.Red },
            new MaskMapPointLabel { LabelId = "blue", Color = System.Drawing.Color.Blue }
        ];
        // 隐藏点刻意放在来源末尾，旧行为会把它绘制在最上层。
        canvas.PointsSource = [visible, hidden];
        canvas.Viewport = new Rect(0, 0, 960, 540);
        Layout(canvas, 960, 540);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, ReadPixel(canvas, 100, 100));
        Assert.Same(visible, canvas.GetPointAt(100, 100));
        Assert.Equal(new[] { visible, hidden }, canvas.GetPointsAt(100, 100));

        visible.IsHidden = true;
        hidden.IsHidden = false;
        canvas.Refresh();
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, ReadPixel(canvas, 100, 100));
        Assert.Same(hidden, canvas.GetPointAt(100, 100));
        Assert.Equal(new[] { hidden, visible }, canvas.GetPointsAt(100, 100));
        Assert.Equal(new[] { visible, hidden }, canvas.PointsSource!);
        return Task.CompletedTask;
    });

    /// <summary>读取离屏渲染中的 BGRA 像素，验证实际绘制顺序而非仅验证排序结果。</summary>
    private static byte[] ReadPixel(PointsCanvas canvas, int x, int y)
    {
        var bitmap = new RenderTargetBitmap((int)canvas.ActualWidth, (int)canvas.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel;
    }

    /// <summary>图标固定为 32 DIP；无论地图缩放，点击圆直径保持 30 DIP，矩形角和边框外不命中。</summary>
    [Theory]
    [InlineData(0.25)]
    [InlineData(1)]
    [InlineData(4)]
    public Task HitTest_UsesInsetScreenCircleAtEveryZoom(double zoom) => RunSta(canvas =>
    {
        var point = PointAt("a", 100 / zoom, 100 / zoom);
        canvas.PointsSource = [point];
        canvas.Viewport = new Rect(0, 0, 960 / zoom, 540 / zoom);
        Layout(canvas, 960, 540);

        Assert.Same(point, canvas.GetPointAt(114.9, 100));
        Assert.Null(canvas.GetPointAt(115.1, 100));
        Assert.Null(canvas.GetPointAt(111, 111));
        return Task.CompletedTask;
    });

    /// <summary>面积比例临界值在圆心距约 12.12 DIP，不能用直径重叠长度或包围矩形代替圆面积。</summary>
    [Theory]
    [InlineData(0, 2)]
    [InlineData(12, 2)]
    [InlineData(12.2, 1)]
    [InlineData(16, 1)]
    [InlineData(30, 1)]
    public Task Candidates_RequireMoreThanHalfCircleArea(double distance, int count) => RunSta(canvas =>
    {
        var selected = PointAt("selected", 100, 100);
        canvas.PointsSource = [PointAt("other", 100 + distance, 100), selected];
        canvas.Viewport = new Rect(0, 0, 960, 540);
        Layout(canvas, 960, 540);

        var candidates = canvas.GetPointsAt(100, 100);
        Assert.Equal(count, candidates.Count);
        Assert.Same(selected, candidates[0]);
        return Task.CompletedTask;
    });

    /// <summary>按屏幕投影计算交叠：同样的地图坐标间距在放大后可能不再属于重叠组。</summary>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    public Task Candidates_UseProjectedDistance(double zoom, int count) => RunSta(canvas =>
    {
        canvas.PointsSource = [PointAt("a", 100, 100), PointAt("b", 110, 100)];
        canvas.Viewport = new Rect(0, 0, 960 / zoom, 540 / zoom);
        Layout(canvas, 960, 540);
        Assert.Equal(count, canvas.GetPointsAt(110 * zoom, 100 * zoom).Count);
        return Task.CompletedTask;
    });

    /// <summary>点击露出边缘时也能选到底层隐藏点；不会沿邻近点把远处图标链式加入菜单。</summary>
    [Fact]
    public Task Candidates_IncludeObscuredPointWithoutExpandingAlongAChain() => RunSta(canvas =>
    {
        var bottom = PointAt("bottom", 112, 100);
        bottom.IsHidden = true;
        var selected = PointAt("top", 100, 100);
        canvas.PointsSource = [PointAt("far", 124, 100), bottom, selected];
        canvas.Viewport = new Rect(0, 0, 960, 540);
        Layout(canvas, 960, 540);

        Assert.Equal(new[] { selected, bottom }, canvas.GetPointsAt(86, 100));
        Assert.Empty(canvas.GetPointsAt(50, 50));
        return Task.CompletedTask;
    });

    /// <summary>控件边缘露出的图标仍能命中；没有有效视口时不能按旧坐标或模型 X/Y 误命中。</summary>
    [Fact]
    public Task HitTest_PreservesVisibleEdgeAndRejectsInvalidViewport() => RunSta(canvas =>
    {
        var point = PointAt("edge", -4, 100);
        canvas.PointsSource = [point];
        canvas.Viewport = new Rect(0, 0, 960, 540);
        Layout(canvas, 960, 540);
        Assert.Same(point, canvas.GetPointAt(2, 100));
        canvas.Viewport = Rect.Empty;
        Assert.Null(canvas.GetPointAt(2, 100));
        canvas.Viewport = new Rect(0, 0, 0, 0);
        Assert.Null(canvas.GetPointAt(2, 100));
        return Task.CompletedTask;
    });

    /// <summary>实际菜单沿用主题完成模板和布局，选择底层同类点时仅执行所选命令，保留图标锚点。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Menu_SelectsRequestedPointAndPreservesLeftAndRightActions(bool rightClick) => RunSta(async canvas =>
    {
        var bottom = PointAt("bottom", 100, 100);
        var top = PointAt("top", 110, 100);
        canvas.PointsSource = [bottom, top];
        canvas.LabelsSource = [new MaskMapPointLabel { LabelId = "label", Name = "宝箱" }];
        canvas.Viewport = new Rect(0, 0, 960, 540);
        Layout(canvas, 960, 540);
        MaskMapPointClickArgs? clicked = null;
        MaskMapPoint? hidden = null;
        canvas.PointClickCommand = new RelayCommand<MaskMapPointClickArgs>(args => clicked = args);
        canvas.PointRightClickCommand = new RelayCommand<MaskMapPoint>(point => hidden = point);

        var menu = canvas.CreateSelectionMenu(canvas.GetPointsAt(110, 100), rightClick);
        menu.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
        menu.Resources.MergedDictionaries.Add(new ControlsDictionary());
        Layout(menu, 320, 200);
        Assert.Equal(2, menu.Items.Count);
        Assert.True(menu.ActualWidth >= 220);
        Assert.False(menu.StaysOpen);
        var item = Assert.IsType<MenuItem>(menu.Items[1]);
        item.ApplyTemplate();
        Assert.NotNull(item.Template);
        var header = Assert.IsType<StackPanel>(item.Header);
        Assert.Equal("宝箱", Assert.IsType<TextBlock>(header.Children[0]).Text);
        Assert.Contains("bottom", Assert.IsType<TextBlock>(header.Children[1]).Text);
        Assert.Null(clicked);
        Assert.Null(hidden);

        await Assert.IsAssignableFrom<IAsyncRelayCommand>(item.Command).ExecuteAsync(null);
        if (rightClick)
        {
            Assert.Same(bottom, hidden);
            Assert.Null(clicked);
        }
        else
        {
            Assert.Same(bottom, clicked!.Point);
            Assert.Equal(new Point(100, 100), clicked.AnchorPosition);
            Assert.Null(hidden);
        }
    });

    /// <summary>单点保持直接打开详情，只有重叠才需要二次选择；空白点击不触发命令。</summary>
    [Fact]
    public Task SinglePoint_ExecutesDirectlyAndBlankDoesNothing() => RunSta(async canvas =>
    {
        var point = PointAt("only", 100, 100);
        canvas.PointsSource = [point];
        canvas.Viewport = new Rect(0, 0, 960, 540);
        Layout(canvas, 960, 540);
        var clicks = new List<MaskMapPointClickArgs>();
        canvas.PointClickCommand = new RelayCommand<MaskMapPointClickArgs>(args => clicks.Add(args!));
        Assert.True(await canvas.HandlePointClickAsync(new Point(100, 100), false));
        Assert.Same(point, Assert.Single(clicks).Point);
        Assert.False(await canvas.HandlePointClickAsync(new Point(200, 100), false));
        Assert.Single(clicks);
    });

    /// <summary>菜单关闭引起的输入焦点恢复必须先完成，再激活遮罩并打开详情，不能让 worker 中途恢复更新。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Menu_TransfersFocusAfterClosingBeforeExecutingPointCommand(bool rightClick) => RunSta(async canvas =>
    {
        var order = new List<string>();
        var trackingCanvas = new FocusTrackingCanvas(order);
        var host = new Border { Child = trackingCanvas };
        try
        {
            var point = PointAt("selected", 100, 100);
            trackingCanvas.PointsSource = [point];
            trackingCanvas.Viewport = new Rect(0, 0, 960, 540);
            Layout(trackingCanvas, 960, 540);
            trackingCanvas.PointClickCommand = new RelayCommand<MaskMapPointClickArgs>(_ => order.Add("point-command"));
            trackingCanvas.PointRightClickCommand = new RelayCommand<MaskMapPoint>(_ => order.Add("point-command"));
            var menu = trackingCanvas.CreateSelectionMenu([point], rightClick);

            // 不显示原生窗口，通过事件模拟关闭动画仍未结束；此时不能提前激活遮罩或执行详情命令。
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            var selection = Assert.IsAssignableFrom<IAsyncRelayCommand>(((MenuItem)menu.Items[0]).Command).ExecuteAsync(null);
            Assert.False(selection.IsCompleted);
            Assert.Empty(order);
            _ = canvas.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                order.Add("menu-focus-restored");
                menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
            }));
            await selection;
            Assert.Equal(new[] { "menu-focus-restored", "overlay-activated", "point-command" }, order);
            Assert.False(menu.IsOpen);
        }
        finally { host.Child = null; }
    });

    /// <summary>记录真实菜单命令对宿主窗口的激活请求，测试不激活系统窗口或操作游戏。</summary>
    private sealed class FocusTrackingCanvas(List<string> order) : PointsCanvas
    {
        /// <inheritdoc />
        protected override void ActivateInteractionWindow() => order.Add("overlay-activated");
    }

    /// <summary>创建明确的地图坐标，避免由命中算法反向生成期望结果。</summary>
    private static MaskMapPoint PointAt(string id, double x, double y) => new()
    {
        Id = id, LabelId = "label", ImageX = x, ImageY = y
    };

    /// <summary>真实执行 WPF 测量与排列，不显示窗口。</summary>
    private static void Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    /// <summary>隔离 STA 调度器，结束后解除画布静态图标事件订阅。</summary>
    private static Task RunSta(Func<PointsCanvas, Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                var canvas = new PointsCanvas();
                var host = new Border { Child = canvas };
                try { await action(canvas); completion.SetResult(); }
                catch (Exception exception) { completion.SetException(exception); }
                finally
                {
                    host.Child = null;
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
