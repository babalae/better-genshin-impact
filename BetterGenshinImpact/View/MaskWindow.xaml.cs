using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Genshin.Settings;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.DpiAwareness;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using Serilog.Sinks.RichTextBox.Abstraction;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BetterGenshinImpact.Genshin.Settings2;
using Vanara.PInvoke;
using FontFamily = System.Windows.Media.FontFamily;

namespace BetterGenshinImpact.View;

/// <summary>
/// 一个用于覆盖在游戏窗口上的窗口，用于显示识别结果、显示日志、设置区域位置等
/// 请使用 Instance 方法获取单例
/// </summary>
public partial class MaskWindow : Window
{
    private static MaskWindow? _maskWindow;

    private static readonly Typeface _typeface;

    private nint _hWnd;

    private IRichTextBox? _richTextBox;

    private readonly ILogger<MaskWindow> _logger = App.GetLogger<MaskWindow>();

    private MaskWindowConfig? _maskWindowConfig;

    static MaskWindow()
    {
        if (Application.Current.TryFindResource("TextThemeFontFamily") is FontFamily fontFamily)
        {
            _typeface = fontFamily.GetTypefaces().First();
        }
        else
        {
            _typeface = new FontFamily("Microsoft Yahei UI").GetTypefaces().First();
        }

        DefaultStyleKeyProperty.OverrideMetadata(typeof(MaskWindow), new FrameworkPropertyMetadata(typeof(MaskWindow)));
    }

    public static MaskWindow Instance()
    {
        if (_maskWindow == null)
        {
            throw new Exception("MaskWindow 未初始化");
        }

        return _maskWindow;
    }

    public bool IsExist()
    {
        return _maskWindow != null && PresentationSource.FromVisual(_maskWindow) != null;
    }

    public void BringToTop()
    {
        User32.BringWindowToTop(new WindowInteropHelper(this).Handle);
    }

    public void RefreshPosition()
    {
        if (TaskContext.Instance().Config.MaskWindowConfig.UseSubform)
        {
            RefreshPositionForSubform();
        }
        else
        {
            RefreshPositionForNormal();
        }
    }

    public void RefreshPositionForNormal()
    {
        var currentRect = SystemControl.GetCaptureRect(TaskContext.Instance().GameHandle);

        Invoke(() =>
        {
            double dpiScale = DpiHelper.ScaleY;

            Left = currentRect.Left / dpiScale;
            Top = currentRect.Top / dpiScale;
            Width = currentRect.Width / dpiScale;
            Height = currentRect.Height / dpiScale;
            BringToTop();
        });
    }

    public void RefreshPositionForSubform()
    {
        nint targetHWnd = TaskContext.Instance().GameHandle;
        _ = User32.GetClientRect(targetHWnd, out RECT targetRect);
        _ = User32.SetWindowPos(_hWnd, IntPtr.Zero, 0, 0, targetRect.Width, targetRect.Height, User32.SetWindowPosFlags.SWP_SHOWWINDOW);
    }

    public MaskWindow()
    {
        _maskWindow = this;

        this.SetResourceReference(StyleProperty, typeof(MaskWindow));
        InitializeComponent();
        this.InitializeDpiAwareness();

        LogTextBox.TextChanged += LogTextBoxTextChanged;
        //AddAreaSettingsControl("测试识别窗口");
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _richTextBox = App.GetService<IRichTextBox>();
        if (_richTextBox != null)
        {
            _richTextBox.RichTextBox = LogTextBox;
        }

        _maskWindowConfig = TaskContext.Instance().Config.MaskWindowConfig;
        _maskWindowConfig.PropertyChanged += MaskWindowConfigOnPropertyChanged;
        UpdateClickThroughState();

        if (TaskContext.Instance().Config.MaskWindowConfig.UseSubform)
        {
            _hWnd = new WindowInteropHelper(this).Handle;
            nint targetHWnd = TaskContext.Instance().GameHandle;

            if (User32.GetParent(_hWnd) != targetHWnd)
            {
                _ = User32.SetParent(_hWnd, targetHWnd);
            }
        }

        RefreshPosition();
        PrintSystemInfo();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_maskWindowConfig != null)
        {
            _maskWindowConfig.PropertyChanged -= MaskWindowConfigOnPropertyChanged;
        }

        base.OnClosed(e);
    }

    private void MaskWindowConfigOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MaskWindowConfig.OverlayLayoutEditEnabled))
        {
            Dispatcher.Invoke(UpdateClickThroughState);
        }
    }

    private void UpdateClickThroughState()
    {
        try
        {
            var editEnabled = TaskContext.Instance().Config.MaskWindowConfig.OverlayLayoutEditEnabled;
            this.SetClickThrough(!editEnabled);
        }
        catch
        {
            this.SetClickThrough(true);
        }
    }

    private void PrintSystemInfo()
    {
        _logger.LogInformation("更好的原神 {Version}", Global.Version);
        var systemInfo = TaskContext.Instance().SystemInfo;
        var width = systemInfo.GameScreenSize.Width;
        var height = systemInfo.GameScreenSize.Height;
        var dpiScale = TaskContext.Instance().DpiScale;
        _logger.LogInformation("遮罩窗口已启动，游戏大小{Width}x{Height}，素材缩放{Scale}，DPI缩放{Dpi}",
            width, height, systemInfo.AssetScale.ToString("F"), dpiScale);

        if (width * 9 != height * 16)
        {
            _logger.LogError("当前游戏分辨率不是16:9，一条龙、配队识别、地图传送、地图追踪等所有独立任务与全自动任务相关功能，都将会无法正常使用！");
        }
        
        AfterburnerWarning();

        // 读取游戏注册表配置
        GameSettingsChecker.LoadGameSettingsAndCheck();
    }
    
    /**
     * MSIAfterburner.exe 在左上角会导致识别失败
     */
    private void AfterburnerWarning()
    {
        if (Process.GetProcessesByName("MSIAfterburner").Length > 0)
        {
            _logger.LogWarning("检测到 MSI Afterburner 正在运行，如果信息位于特定UI上遮盖图像识别要素可能导致识别失败，请关闭MSI Afterburner 或者调整信息位置后重试！");
        }
    }

    // private void ReadGameSettings()
    // {
    //     try
    //     {
    //         SettingsContainer settings = new();
    //         TaskContext.Instance().GameSettings = settings;
    //         var lang = settings.Language?.TextLang;
    //         if (lang != null && lang != TextLanguage.SimplifiedChinese)
    //         {
    //             _logger.LogWarning("当前游戏语言{Lang}不是简体中文，部分功能可能无法正常使用。The game language is not Simplified Chinese, some functions may not work properly", lang);
    //         }
    //     }
    //     catch (Exception e)
    //     {
    //         _logger.LogWarning("游戏注册表配置信息读取失败：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
    //     }
    // }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        this.SetLayeredWindow();
        this.SetChildWindow();
        this.HideFromAltTab();
        UpdateClickThroughState();
    }

    private void LogTextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (LogTextBox.Document.Blocks.FirstBlock is Paragraph p && p.Inlines.Count > 100)
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

    public void Refresh()
    {
        Dispatcher.Invoke(InvalidateVisual);
    }

    public void Invoke(Action action)
    {
        Dispatcher.Invoke(action);
    }

    public void HideSelf()
    {
        if (TaskContext.Instance().Config.MaskWindowConfig.OverlayLayoutEditEnabled)
        {
            return;
        }
        this.Hide();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        try
        {
            var cnt = VisionContext.Instance().DrawContent.RectList.Count + VisionContext.Instance().DrawContent.LineList.Count + VisionContext.Instance().DrawContent.TextList.Count;
            if (cnt == 0)
            {
                return;
            }

            // 先有上方判断的原因是，有可能Render的时候，配置还未初始化
            if (!TaskContext.Instance().Config.MaskWindowConfig.DisplayRecognitionResultsOnMask)
            {
                return;
            }

            foreach (var kv in VisionContext.Instance().DrawContent.RectList)
            {
                foreach (var drawable in kv.Value)
                {
                    if (!drawable.IsEmpty)
                    {
                        drawingContext.DrawRectangle(Brushes.Transparent,
                            new Pen(new SolidColorBrush(drawable.Pen.Color.ToWindowsColor()), drawable.Pen.Width),
                            drawable.Rect);
                    }
                }
            }

            foreach (var kv in VisionContext.Instance().DrawContent.LineList)
            {
                foreach (var drawable in kv.Value)
                {
                    drawingContext.DrawLine(new Pen(new SolidColorBrush(drawable.Pen.Color.ToWindowsColor()), drawable.Pen.Width), drawable.P1, drawable.P2);
                }
            }

            foreach (var kv in VisionContext.Instance().DrawContent.TextList)
            {
                foreach (var drawable in kv.Value)
                {
                    if (!drawable.IsEmpty)
                    {
                        drawingContext.DrawText(new FormattedText(drawable.Text,
                            CultureInfo.GetCultureInfo("zh-cn"),
                            FlowDirection.LeftToRight,
                            _typeface,
                            36, Brushes.Black, 1), drawable.Point);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }

        base.OnRender(drawingContext);
    }

    public RichTextBox LogBox => LogTextBox;
}

file static class MaskWindowExtension
{
    public static void HideFromAltTab(this Window window)
    {
        HideFromAltTab(new WindowInteropHelper(window).Handle);
    }

    public static void HideFromAltTab(nint hWnd)
    {
        int style = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);

        style |= (int)User32.WindowStylesEx.WS_EX_TOOLWINDOW;
        User32.SetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE, style);
    }

    public static void SetLayeredWindow(this Window window, bool isLayered = true)
    {
        SetLayeredWindow(new WindowInteropHelper(window).Handle, isLayered);
    }

    private static void SetLayeredWindow(nint hWnd, bool isLayered = true)
    {
        int style = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);

        if (isLayered)
        {
            style |= (int)User32.WindowStylesEx.WS_EX_TRANSPARENT;
            style |= (int)User32.WindowStylesEx.WS_EX_LAYERED;
        }
        else
        {
            style &= ~(int)User32.WindowStylesEx.WS_EX_TRANSPARENT;
            style &= ~(int)User32.WindowStylesEx.WS_EX_LAYERED;
        }

        _ = User32.SetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE, style);
    }

    public static void SetClickThrough(this Window window, bool isClickThrough)
    {
        SetClickThrough(new WindowInteropHelper(window).Handle, isClickThrough);
    }

    private static void SetClickThrough(nint hWnd, bool isClickThrough)
    {
        int style = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);

        if (isClickThrough)
        {
            style |= (int)User32.WindowStylesEx.WS_EX_TRANSPARENT;
        }
        else
        {
            style &= ~(int)User32.WindowStylesEx.WS_EX_TRANSPARENT;
        }

        _ = User32.SetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE, style);
    }

    public static void SetChildWindow(this Window window)
    {
        SetChildWindow(new WindowInteropHelper(window).Handle);
    }

    private static void SetChildWindow(nint hWnd)
    {
        int style = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_STYLE);

        style |= (int)User32.WindowStyles.WS_CHILD;
        _ = User32.SetWindowLong(hWnd, User32.WindowLongFlags.GWL_STYLE, style);
    }
}
