using BetterGenshinImpact.GameTask.Model.Area;
using Fischless.WindowsInput;
using OpenCvSharp;
using System.Diagnostics;
using Vanara.PInvoke;
using Size = System.Drawing.Size;

namespace BetterGenshinImpact.GameTask.Model;

/// <summary>
/// 云原神会话使用的固定 1920×1080 系统信息。
/// 截图坐标始终以画面左上角 (0,0) 为原点，不受父窗口屏幕位置和显示状态影响；
/// DesktopRegion 持有云鼠标模拟器，从而复用现有 Region 坐标转换链。
/// </summary>
public sealed class CloudGameSystemInfo : ISystemInfo
{
    /// <summary>
    /// 创建云会话系统信息，并将 DesktopRegion 绑定到当前会话的云鼠标。
    /// </summary>
    /// <param name="mouse">当前云会话的鼠标模拟器。</param>
    /// <param name="browserProcessId">WebView2 浏览器进程 ID，仅用于兼容现有进程信息接口。</param>
    public CloudGameSystemInfo(IMouseSimulator mouse, int browserProcessId)
    {
        // 优先保留真实 WebView2 浏览器进程；极端情况下回退到当前进程，避免接口出现空值。
        GameProcess = browserProcessId > 0
            ? Process.GetProcessById(browserProcessId)
            : Process.GetCurrentProcess();
        GameProcessName = GameProcess.ProcessName;
        GameProcessId = GameProcess.Id;

        // 云画面坐标固定为 1920×1080，不叠加父窗口在桌面上的偏移。
        DesktopRectArea = new DesktopRegion(1920, 1080, mouse);
    }

    /// <summary>
    /// 云画面的固定显示尺寸。
    /// </summary>
    public Size DisplaySize { get; } = new(1920, 1080);

    /// <summary>
    /// 云画面本地坐标矩形。
    /// </summary>
    public RECT GameScreenSize { get; } = new(0, 0, 1920, 1080);

    /// <summary>
    /// 识图素材缩放比例；云画面固定为 1080P，因此恒为 1。
    /// </summary>
    public double AssetScale { get; } = 1d;

    /// <summary>
    /// 最大 1080P 缩放比例。
    /// </summary>
    public double ZoomOutMax1080PRatio { get; } = 1d;

    /// <summary>
    /// 转换到 1080P 的缩放比例。
    /// </summary>
    public double ScaleTo1080PRatio { get; } = 1d;

    /// <summary>
    /// 截图区域始终覆盖完整云画面。
    /// </summary>
    public RECT CaptureAreaRect { get; set; } = new(0, 0, 1920, 1080);

    /// <summary>
    /// OpenCV 使用的完整 1080P 截图矩形。
    /// </summary>
    public Rect ScaleMax1080PCaptureRect { get; set; } = new(0, 0, 1920, 1080);

    /// <summary>
    /// WebView2 浏览器进程对象。
    /// </summary>
    public Process GameProcess { get; }

    /// <summary>
    /// WebView2 浏览器进程名称。
    /// </summary>
    public string GameProcessName { get; }

    /// <summary>
    /// WebView2 浏览器进程 ID。
    /// </summary>
    public int GameProcessId { get; }

    /// <summary>
    /// 使用云鼠标后端的桌面坐标区域。
    /// </summary>
    public DesktopRegion DesktopRectArea { get; }
}
