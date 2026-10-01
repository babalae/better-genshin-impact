using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using BetterGenshinImpact.Helpers;
using Fischless.GameCapture;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Model.Area;

/// <summary>
/// 桌面区域类
/// 无缩放的桌面屏幕大小
/// 主要用于点击操作，鼠标每次都从 InputHub.Foreground 取，后端切换后自动生效
/// </summary>
public class DesktopRegion : Region
{
    private static IMouseInput Mouse => InputHub.Foreground.Mouse;

    public DesktopRegion(int w, int h) : base(0, 0, w, h)
    {
    }

    public DesktopRegion() : base(0, 0, PrimaryScreen.WorkingArea.Width, PrimaryScreen.WorkingArea.Height)
    {
    }

    /// <summary>
    /// 截图区域树的根。从它派生的所有区域都绘制到这个遮罩窗口绘制入口
    /// </summary>
    public DesktopRegion(IMaskWindowDrawingBoard? drawingBoard)
        : base(0, 0, PrimaryScreen.WorkingArea.Width, PrimaryScreen.WorkingArea.Height, drawingBoard: drawingBoard)
    {
    }

    public void DesktopRegionClick(int x, int y, int w, int h)
    {
        Mouse.MoveMouseTo((x + (w * 1d / 2)) * 65535 / Width,
            (y + (h * 1d / 2)) * 65535 / Height).LeftButtonDown().Sleep(50).LeftButtonUp().Sleep(50);
    }

    public void DesktopRegionMove(int x, int y, int w, int h)
    {
        Mouse.MoveMouseTo((x + (w * 1d / 2)) * 65535 / Width,
            (y + (h * 1d / 2)) * 65535 / Height);
    }

    /// <summary>
    /// 静态方法,每次都会重新计算屏幕大小
    /// </summary>
    /// <param name="cx"></param>
    /// <param name="cy"></param>
    public static void DesktopRegionClick(double cx, double cy)
    {
        Mouse.MoveMouseTo(cx * 65535 * 1d / PrimaryScreen.WorkingArea.Width,
            cy * 65535 * 1d / PrimaryScreen.WorkingArea.Height).LeftButtonDown().Sleep(50).LeftButtonUp().Sleep(50);
    }

    public static void DesktopRegionMove(double cx, double cy)
    {
        Mouse.MoveMouseTo(cx * 65535 * 1d / PrimaryScreen.WorkingArea.Width,
            cy * 65535 * 1d / PrimaryScreen.WorkingArea.Height);
    }
    
    public static void DesktopRegionMoveBy(double dx, double dy)
    {
        Mouse.MoveMouseBy((int)dx, (int)dy);
    }

    public GameCaptureRegion Derive(Mat captureMat, int x, int y)
    {
        return new GameCaptureRegion(captureMat, x, y, this, new TranslationConverter(x, y));
    }
}
