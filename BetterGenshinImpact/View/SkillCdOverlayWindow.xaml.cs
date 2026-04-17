using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.DpiAwareness;
using BetterGenshinImpact.View.Drawable;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Vanara.PInvoke;
using FontFamily = System.Windows.Media.FontFamily;

namespace BetterGenshinImpact.View;

/// <summary>
/// 技能 CD 覆盖层窗口
/// </summary>
public partial class SkillCdOverlayWindow : Window
{
    private static SkillCdOverlayWindow? _instance;

    private static readonly Typeface _fgiTypeface;

    private readonly List<TextDrawable> _texts = new();
    private readonly object _textsLock = new();

    static SkillCdOverlayWindow()
    {
        try
        {
            _fgiTypeface = new FontFamily(new Uri("pack://application:,,,/"), "./Resources/Fonts/Fgi-Regular.ttf#Fgi-Regular").GetTypefaces().First();
        }
        catch
        {
            _fgiTypeface = new FontFamily("Microsoft Yahei UI").GetTypefaces().First();
        }
    }

    private SkillCdOverlayWindow()
    {
        InitializeComponent();
        this.InitializeDpiAwareness();
    }

    public static SkillCdOverlayWindow? InstanceNullable() => _instance;

    public static void CreateInstance()
    {
        if (_instance != null)
        {
            return;
        }

        _instance = new SkillCdOverlayWindow();
    }

    public static void DestroyInstance()
    {
        if (_instance == null)
        {
            return;
        }

        if (_instance.IsVisible)
        {
            _instance.Hide();
        }

        _instance.Close();
        _instance = null;
    }

    /// <summary>
    /// 更新 CD 文本列表
    /// </summary>
    public void UpdateTexts(List<TextDrawable>? texts)
    {
        lock (_textsLock)
        {
            _texts.Clear();
            if (texts != null)
            {
                _texts.AddRange(texts);
            }
        }

        Invoke(InvalidateVisual);
    }

    public void RefreshPosition()
    {
        var currentRect = SystemControl.GetCaptureRect(TaskContext.Instance().GameHandle);

        Invoke(() =>
        {
            double dpiScale = DpiHelper.ScaleY;

            Left = currentRect.Left / dpiScale;
            Top = currentRect.Top / dpiScale;
            Width = currentRect.Width / dpiScale;
            Height = currentRect.Height / dpiScale;
        });
    }

    public void Invoke(Action action)
    {
        try
        {
            Dispatcher.Invoke(action);
        }
        catch (TaskCanceledException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        this.SetLayeredWindow();
        this.SetChildWindow();
        this.HideFromAltTab();
        this.SetClickThrough(true);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        List<TextDrawable> snapshot;
        lock (_textsLock)
        {
            snapshot = new List<TextDrawable>(_texts);
        }

        if (snapshot.Count == 0)
        {
            return;
        }

        try
        {
            var systemInfo = TaskContext.Instance().SystemInfo;
            var scaleTo1080 = systemInfo.ScaleTo1080PRatio;
            var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var skillConfig = TaskContext.Instance().Config.SkillCdConfig;

            var mediumTypeface = new Typeface(_fgiTypeface.FontFamily, _fgiTypeface.Style, FontWeights.Medium, _fgiTypeface.Stretch);
            double scaledFontSize = (26 * scaleTo1080 * skillConfig.Scale) / pixelsPerDip;

            foreach (var drawable in snapshot)
            {
                if (drawable.IsEmpty)
                {
                    continue;
                }

                var renderPoint = new Point(drawable.Point.X / pixelsPerDip, drawable.Point.Y / pixelsPerDip);

                bool isZeroCd =
                    double.TryParse(drawable.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var cdValue)
                    && Math.Abs(cdValue) < 0.8;

                string textColorStr = isZeroCd ? skillConfig.TextReadyColor : skillConfig.TextNormalColor;
                string bgColorStr = isZeroCd ? skillConfig.BackgroundReadyColor : skillConfig.BackgroundNormalColor;

                Color textColor = ParseColor(textColorStr) ?? (isZeroCd ? Color.FromRgb(93, 204, 23) : Color.FromRgb(218, 74, 35));
                Color bgColor = ParseColor(bgColorStr) ?? Colors.White;

                Brush textBrush = new SolidColorBrush(textColor);
                Brush bgBrush = new SolidColorBrush(bgColor);

                var formattedText = new FormattedText(
                    drawable.Text,
                    CultureInfo.GetCultureInfo("zh-cn"),
                    FlowDirection.LeftToRight,
                    mediumTypeface,
                    scaledFontSize,
                    textBrush,
                    pixelsPerDip);

                double px = (6 * scaleTo1080 * skillConfig.Scale) / pixelsPerDip;
                double py = (2 * scaleTo1080 * skillConfig.Scale) / pixelsPerDip;
                double radius = (5 * scaleTo1080 * skillConfig.Scale) / pixelsPerDip;
                var bgRect = new Rect(renderPoint.X - px, renderPoint.Y - py, formattedText.Width + px * 2, formattedText.Height + py * 2);
                drawingContext.DrawRoundedRectangle(bgBrush, null, bgRect, radius, radius);
                drawingContext.DrawText(formattedText, renderPoint);
            }
        }
        catch
        {
        }

        base.OnRender(drawingContext);
    }

    private static Color? ParseColor(string colorStr)
    {
        if (string.IsNullOrWhiteSpace(colorStr))
        {
            return null;
        }

        try
        {
            string hex = colorStr.Trim().TrimStart('#');

            if (hex.Length == 6)
            {
                byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                return Color.FromArgb(255, r, g, b);
            }
            else if (hex.Length == 8)
            {
                byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                byte a = byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber);
                return Color.FromArgb(a, r, g, b);
            }
        }
        catch
        {
        }

        return null;
    }
}

file static class SkillCdOverlayWindowExtension
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
        SetLayeredWindow(new WindowInteropHelper(window).Handle, isClickThrough);
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
