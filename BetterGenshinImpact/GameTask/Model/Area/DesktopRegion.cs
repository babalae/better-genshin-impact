using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using BetterGenshinImpact.Helpers;
using Fischless.WindowsInput;
using System.Drawing;
using Fischless.GameCapture;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Model.Area;

/// <summary>
/// 桌面区域类
/// 无缩放的桌面屏幕大小
/// 主要用于点击操作
/// </summary>
public class DesktopRegion : Region
{
    private readonly IMouseSimulator mouse;
    
    public DesktopRegion(int w, int h, IMouseSimulator? iMouse = null) : base(0, 0, w, h)
    {
        mouse = iMouse ?? Simulation.SendInput.Mouse;
    }
    
    public DesktopRegion() : base(0, 0, PrimaryScreen.WorkingArea.Width, PrimaryScreen.WorkingArea.Height)
    {
        mouse = Simulation.SendInput.Mouse;
    }

    public DesktopRegion(IMouseSimulator mouse) : base(0, 0, PrimaryScreen.WorkingArea.Width, PrimaryScreen.WorkingArea.Height)
    {
        this.mouse = mouse;
    }


    public void DesktopRegionClick(int x, int y, int w, int h)
    {
        if (mouse == null)
        {
            throw new System.NullReferenceException();
        }
        mouse.MoveMouseTo((x + (w * 1d / 2)) * 65535 / Width,
            (y + (h * 1d / 2)) * 65535 / Height).LeftButtonClick().Sleep(50).LeftButtonUp();
    }

    public void DesktopRegionMove(int x, int y, int w, int h)
    {
        if (mouse == null)
        {
            throw new System.NullReferenceException();
        }
        mouse.MoveMouseTo((x + (w * 1d / 2)) * 65535 / Width,
            (y + (h * 1d / 2)) * 65535 / Height);
    }

    /// <summary>
    /// 静态方法,每次都会重新计算屏幕大小
    /// </summary>
    /// <param name="cx"></param>
    /// <param name="cy"></param>
    public static void DesktopRegionClick(double cx, double cy)
    {
        Simulation.SendInput.Mouse.MoveMouseTo(cx * 65535 * 1d / PrimaryScreen.WorkingArea.Width,
            cy * 65535 * 1d / PrimaryScreen.WorkingArea.Height).LeftButtonDown().Sleep(50).LeftButtonUp();
    }

    public static void DesktopRegionMove(double cx, double cy)
    {
        Simulation.SendInput.Mouse.MoveMouseTo(cx * 65535 * 1d / PrimaryScreen.WorkingArea.Width,
            cy * 65535 * 1d / PrimaryScreen.WorkingArea.Height);
    }
    
    public static void DesktopRegionMoveBy(double dx, double dy)
    {
        Simulation.SendInput.Mouse.MoveMouseBy((int)dx, (int)dy);
    }

    public GameCaptureRegion Derive(Mat captureMat, int x, int y)
    {
        return new GameCaptureRegion(captureMat, x, y, this, new TranslationConverter(x, y));
    }
}
