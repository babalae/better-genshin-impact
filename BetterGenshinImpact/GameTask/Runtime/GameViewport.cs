using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.Runtime;

/// <summary>
/// 游戏画面在屏幕上的区域
/// </summary>
/// <param name="ScreenRect">物理像素矩形，尺寸与截图帧一致（即 SystemInfo.CaptureAreaRect）</param>
/// <param name="DpiScale">画面所在显示器的缩放比例</param>
public readonly record struct GameViewport(RECT ScreenRect, float DpiScale)
{
    public int Width => ScreenRect.Width;

    public int Height => ScreenRect.Height;
}
