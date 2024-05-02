using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using BetterGenshinImpact.Helpers;
using System.Drawing;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Model.Area;

/// <summary>
/// 桌面区域类
/// 无缩放的桌面屏幕大小
/// 主要用于点击操作
/// </summary>
public class DesktopRegion() : Region(0, 0, PrimaryScreen.WorkingArea.Width, PrimaryScreen.WorkingArea.Height)
{
    public void DesktopRegionClick(int x, int y, int w, int h)
    {
        Simulation.SendInputEx.Mouse.MoveMouseTo((x + (w * 1d / 2)) * 65535 / Width,
            (y + (h * 1d / 2)) * 65535 / Height).LeftButtonClick().Sleep(50).LeftButtonUp();
    }

    public GameCaptureRegion Derive(Bitmap captureBitmap, int x, int y)
    {
        return new GameCaptureRegion(captureBitmap, x, y, this, new TranslationConverter(x, y));
    }
}
