using System;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BetterGenshinImpact.Service.Instance;

/// <summary>
/// 描述当前实例在窗口、标题栏和托盘中共用的视觉身份。
/// </summary>
public sealed class InstanceVisualIdentity
{
    /// <summary>
    /// 运行时图标的基准像素尺寸。
    /// </summary>
    private const int IconSize = 128;

    /// <summary>
    /// 作为所有实例图标主体的现有 BetterGI Logo 资源地址。
    /// </summary>
    private static readonly Uri LogoUri = new("pack://application:,,,/Resources/Images/logo.png", UriKind.Absolute);

    /// <summary>
    /// 初始化不可变的实例视觉身份。
    /// </summary>
    private InstanceVisualIdentity(
        bool isPrimary,
        string applicationTitle,
        string displayName,
        string windowTitle,
        string trayTooltipText,
        Brush accentBrush,
        ImageSource defaultIconSource,
        ImageSource iconSource)
    {
        IsPrimary = isPrimary;
        ApplicationTitle = applicationTitle;
        DisplayName = displayName;
        WindowTitle = windowTitle;
        TrayTooltipText = trayTooltipText;
        AccentBrush = accentBrush;
        DefaultIconSource = defaultIconSource;
        IconSource = iconSource;
    }

    /// <summary>
    /// 当前实例是否为主实例。
    /// </summary>
    public bool IsPrimary { get; }

    /// <summary>
    /// 不包含实例身份的应用标题。
    /// </summary>
    public string ApplicationTitle { get; }

    /// <summary>
    /// 当前实例在标题栏标签和菜单中展示的名称。
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// 当前实例在 Windows 标题、任务栏和 Alt+Tab 中展示的完整标题。
    /// </summary>
    public string WindowTitle { get; }

    /// <summary>
    /// 当前实例的托盘提示文本。
    /// </summary>
    public string TrayTooltipText { get; }

    /// <summary>
    /// 当前实例的稳定强调色画刷。
    /// </summary>
    public Brush AccentBrush { get; }

    /// <summary>
    /// 不包含任何实例身份特征的原始 BetterGI Logo。
    /// </summary>
    public ImageSource DefaultIconSource { get; }

    /// <summary>
    /// 基于 BetterGI Logo、实例类型角标和实例强调色生成的图标。
    /// </summary>
    public ImageSource IconSource { get; }

    /// <summary>
    /// 根据实例上下文创建在当前进程生命周期内保持不变的视觉身份。
    /// </summary>
    /// <param name="context">当前实例上下文。</param>
    /// <param name="applicationTitle">应用标题和版本信息。</param>
    /// <returns>可供窗口和托盘共同绑定的视觉身份。</returns>
    public static InstanceVisualIdentity Create(InstanceContext context, string applicationTitle)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationTitle);

        var displayName = GetDisplayName(context);
        var windowIdentity = GetWindowIdentity(context, displayName);
        var accentColor = GetAccentColor(context);
        var accentBrush = new SolidColorBrush(accentColor);
        accentBrush.Freeze();
        var logo = LoadLogo();

        return new InstanceVisualIdentity(
            context.IsRoot,
            applicationTitle,
            displayName,
            $"{windowIdentity} — {applicationTitle}",
            $"{displayName} — BetterGI",
            accentBrush,
            logo,
            CreateIcon(logo, context.InstanceType, accentColor));
    }

    /// <summary>
    /// 构造适合在紧凑标签和菜单中展示的实例名称。
    /// </summary>
    private static string GetDisplayName(InstanceContext context)
    {
        return context.InstanceType switch
        {
            BetterGiInstanceType.Primary => "主实例",
            BetterGiInstanceType.ChildSession => $"桌面分身 · 会话 {context.WindowsSessionId}",
            BetterGiInstanceType.WebView => $"云原神 · {context.InstanceName ?? "未命名"}",
            _ => "BetterGI 实例"
        };
    }

    /// <summary>
    /// 构造靠前显示实例差异、便于从任务栏和 Alt+Tab 快速识别的标题前缀。
    /// </summary>
    private static string GetWindowIdentity(InstanceContext context, string displayName)
    {
        return context.InstanceType == BetterGiInstanceType.WebView && !string.IsNullOrWhiteSpace(context.InstanceName)
            ? $"{context.InstanceName} · 云原神"
            : displayName;
    }

    /// <summary>
    /// 主实例使用舒适蓝；桌面分身使用青绿系，云原神使用暖色系。
    /// </summary>
    private static Color GetAccentColor(InstanceContext context)
    {
        if (context.IsRoot)
        {
            return Color.FromRgb(79, 126, 219);
        }

        var stableKey = context.InstanceType switch
        {
            BetterGiInstanceType.ChildSession => $"child-session:{context.WindowsSessionId}",
            BetterGiInstanceType.WebView => $"web-view:{context.InstanceName?.Trim().ToUpperInvariant()}",
            _ => $"instance:{context.InstanceType}:{context.WindowsSessionId}"
        };
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(stableKey));
        var colors = context.InstanceType == BetterGiInstanceType.WebView
            ? WebViewInstanceColors
            : ChildSessionInstanceColors;
        var colorIndex = (hash[0] << 8 | hash[1]) % colors.Length;
        return colors[colorIndex];
    }

    /// <summary>
    /// 桌面分身使用的柔和青绿系强调色。
    /// </summary>
    private static readonly Color[] ChildSessionInstanceColors =
    [
        Color.FromRgb(70, 148, 139),
        Color.FromRgb(104, 151, 91),
        Color.FromRgb(72, 139, 113),
        Color.FromRgb(92, 146, 123)
    ];

    /// <summary>
    /// 云原神实例使用的柔和暖色系强调色，避免与主实例蓝色混淆。
    /// </summary>
    private static readonly Color[] WebViewInstanceColors =
    [
        Color.FromRgb(201, 111, 91),
        Color.FromRgb(190, 92, 122),
        Color.FromRgb(181, 131, 69),
        Color.FromRgb(165, 107, 159),
        Color.FromRgb(194, 125, 79),
        Color.FromRgb(174, 95, 105)
    ];

    /// <summary>
    /// 加载并冻结原始 BetterGI Logo，供单实例模式和角标图标共同使用。
    /// </summary>
    private static ImageSource LoadLogo()
    {
        var logo = new BitmapImage();
        logo.BeginInit();
        logo.UriSource = LogoUri;
        logo.CacheOption = BitmapCacheOption.OnLoad;
        logo.EndInit();
        logo.Freeze();
        return logo;
    }

    /// <summary>
    /// 在原 Logo 右下角叠加类型与实例色角标，生成窗口与托盘共用图标。
    /// </summary>
    private static ImageSource CreateIcon(
        ImageSource logo,
        BetterGiInstanceType instanceType,
        Color accentColor)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var drawingContext = visual.RenderOpen())
        {
            var accentBrush = new SolidColorBrush(accentColor);
            var whiteBrush = Brushes.White;

            drawingContext.DrawImage(logo, new Rect(0d, 0d, IconSize, IconSize));

            var badgeCenter = new Point(98d, 98d);
            drawingContext.DrawEllipse(accentBrush, new Pen(whiteBrush, 5d), badgeCenter, 25d, 25d);
            DrawTypeGlyph(drawingContext, instanceType, badgeCenter, whiteBrush);
        }

        var bitmap = new RenderTargetBitmap(IconSize, IconSize, 96d, 96d, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// 用简化图形绘制主实例星标、桌面分身显示器或网页版云朵角标。
    /// </summary>
    private static void DrawTypeGlyph(
        DrawingContext drawingContext,
        BetterGiInstanceType instanceType,
        Point center,
        Brush glyphBrush)
    {
        switch (instanceType)
        {
            case BetterGiInstanceType.Primary:
                DrawPrimaryGlyph(drawingContext, center, glyphBrush);
                break;
            case BetterGiInstanceType.ChildSession:
                DrawDesktopGlyph(drawingContext, center, glyphBrush);
                break;
            case BetterGiInstanceType.WebView:
                DrawCloudGlyph(drawingContext, center, glyphBrush);
                break;
        }
    }

    /// <summary>
    /// 绘制主实例使用的四芒星角标。
    /// </summary>
    private static void DrawPrimaryGlyph(DrawingContext drawingContext, Point center, Brush glyphBrush)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(center.X, center.Y - 15d), true, true);
            context.LineTo(new Point(center.X + 5d, center.Y - 5d), true, false);
            context.LineTo(new Point(center.X + 15d, center.Y), true, false);
            context.LineTo(new Point(center.X + 5d, center.Y + 5d), true, false);
            context.LineTo(new Point(center.X, center.Y + 15d), true, false);
            context.LineTo(new Point(center.X - 5d, center.Y + 5d), true, false);
            context.LineTo(new Point(center.X - 15d, center.Y), true, false);
            context.LineTo(new Point(center.X - 5d, center.Y - 5d), true, false);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(glyphBrush, null, geometry);
    }

    /// <summary>
    /// 绘制桌面分身使用的显示器角标。
    /// </summary>
    private static void DrawDesktopGlyph(DrawingContext drawingContext, Point center, Brush glyphBrush)
    {
        var glyphPen = new Pen(glyphBrush, 4d)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        glyphPen.Freeze();

        drawingContext.DrawRoundedRectangle(
            null,
            glyphPen,
            new Rect(center.X - 14d, center.Y - 11d, 28d, 20d),
            3d,
            3d);
        drawingContext.DrawLine(glyphPen, new Point(center.X, center.Y + 10d), new Point(center.X, center.Y + 15d));
        drawingContext.DrawLine(glyphPen, new Point(center.X - 7d, center.Y + 15d), new Point(center.X + 7d, center.Y + 15d));
    }

    /// <summary>
    /// 绘制网页版实例使用的云朵角标。
    /// </summary>
    private static void DrawCloudGlyph(DrawingContext drawingContext, Point center, Brush glyphBrush)
    {
        drawingContext.DrawEllipse(glyphBrush, null, new Point(center.X - 8d, center.Y + 2d), 9d, 8d);
        drawingContext.DrawEllipse(glyphBrush, null, new Point(center.X, center.Y - 4d), 11d, 11d);
        drawingContext.DrawEllipse(glyphBrush, null, new Point(center.X + 10d, center.Y + 3d), 8d, 7d);
        drawingContext.DrawRoundedRectangle(
            glyphBrush,
            null,
            new Rect(center.X - 16d, center.Y, 32d, 11d),
            5d,
            5d);
    }
}
