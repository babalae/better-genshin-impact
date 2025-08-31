using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Helpers.Extensions;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.Core.Recognition;
using Fischless.GameCapture;
using OpenCvSharp;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoWood.Utils;

internal sealed class Login3rdParty
{
    public enum The3rdPartyType
    {
        None,
        Bilibili,
    }

    public bool IsAvailabled => Type != The3rdPartyType.None;
    public The3rdPartyType Type { get; private set; } = default;
    private (double x1080, double y1080)? lastAgreementClickPos = null;

    public void RefreshAvailabled()
    {
        Type = The3rdPartyType.None;

        try
        {
            if (Process.GetProcessesByName("YuanShen").FirstOrDefault() is Process p)
            {
                uint tid = User32.GetWindowThreadProcessId(p.MainWindowHandle, out uint pid);

                if (tid != 0)
                {
                    using Kernel32.SafeHPROCESS hProcess = Kernel32.OpenProcess(new ACCESS_MASK(Kernel32.ProcessAccess.PROCESS_QUERY_INFORMATION), false, pid);

                    if (!hProcess.IsInvalid)
                    {
                        StringBuilder devicePath = new(260);
                        uint size = (uint)devicePath.Capacity;

                        if (Kernel32.QueryFullProcessImageName(hProcess, 0, devicePath, ref size))
                        {
                            FileInfo fileInfo = new(devicePath.ToString());

                            if (fileInfo.Exists)
                            {
                                string? configIni = Path.Combine(fileInfo.DirectoryName!, "config.ini");
                                string[] lines = File.ReadAllLines(configIni);

                                foreach (string line in lines)
                                {
                                    string kv = line.Trim();
                                    if (kv.StartsWith("channel=") && kv.EndsWith("14"))
                                    {
                                        Type = The3rdPartyType.Bilibili;
                                        break;
                                    }
                                }
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"Error getting process image file name. Error code: {Marshal.GetLastWin32Error()}");
                        }
                    }
                }
            }
        }
        catch
        {
            ///
        }
    }

    public void Login(CancellationToken ct)
    {
        int failCount = default;

        while (!LoginPrivate(ct))
        {
            // It is necessary to support exitable trying.
            // Can exit trying when over than 10s.
            if (++failCount > 20)
            {
                Debug.WriteLine("[AutoWood] Give up to check login button and don't try again.");
                break;
            }

            Debug.WriteLine($"[AutoWood] Fail to check login button {failCount} time(s).");
            Sleep(500, ct);
        }

        Debug.WriteLine("[AutoWood] Exit while check login button.");
    }

    private bool LoginPrivate(CancellationToken ct)
    {
        if (Type == The3rdPartyType.Bilibili)
        {
            if (Process.GetProcessesByName("YuanShen").FirstOrDefault() is Process process)
            {
                // 使用新的B服登录逻辑
                var (loginWindow, windowType) = GetBiliLoginWindow(process);
                if (loginWindow != IntPtr.Zero)
                {
                    if (windowType.Contains("协议"))
                    {
                        ImageRegion screen;
                        try
                        {
                            screen = CaptureWindowToRectArea(loginWindow);
                        }
                        catch
                        {
                            screen = CaptureToRectArea();
                        }
                        var ocrList = screen.FindMulti(RecognitionObject.OcrThis);
                        var agreeRegion = ocrList.FirstOrDefault(r =>
                            r.Text.Contains("同意") && !r.Text.Contains("不同意"));
                        if (agreeRegion != null)
                        {
                            ClickRegionCenterBy1080(agreeRegion);
                            // 记录协议窗口点击位置
                            var (centerDesktopX, centerDesktopY) = agreeRegion.ConvertPositionToDesktopRegion(agreeRegion.Width / 2, agreeRegion.Height / 2);
                            var captureRect = TaskContext.Instance().SystemInfo.CaptureAreaRect;
                            var inCaptureX = centerDesktopX - captureRect.X;
                            var inCaptureY = centerDesktopY - captureRect.Y;
                            var scale = TaskContext.Instance().SystemInfo.ScaleTo1080PRatio;
                            lastAgreementClickPos = (inCaptureX / scale, inCaptureY / scale);
                            Debug.WriteLine("[AutoWood] Click protocol window for Bilibili using OCR");
                        }

                        Thread.Sleep(2000);

                        // 检查窗口是否还存在
                        var (remainingWindow, remainingType) = GetBiliLoginWindow(process);
                        if (remainingWindow == IntPtr.Zero || !remainingType.Contains("协议"))
                        {
                            // 协议窗口已消失，继续等待登录窗口
                            return false; // 继续循环等待登录窗口
                        }

                        return false; // 协议窗口仍然存在，继续尝试
                    }
                    if (windowType.Contains("登录"))
                    {
                        Thread.Sleep(2000);
                        // 使用协议窗口坐标或默认坐标点击登录
                        if (lastAgreementClickPos.HasValue)
                        {
                            GameCaptureRegion.GameRegion1080PPosClick(lastAgreementClickPos.Value.x1080, lastAgreementClickPos.Value.y1080);
                            Debug.WriteLine("[AutoWood] Click login window for Bilibili");
                        }
                        else
                        {
                            GameCaptureRegion.GameRegion1080PPosClick(960, 620);
                            Debug.WriteLine("[AutoWood] Click login window for Bilibili");
                        }

                        Thread.Sleep(2000);

                        // 检查窗口是否还存在
                        var (remainingWindow, remainingType) = GetBiliLoginWindow(process);
                        if (remainingWindow == IntPtr.Zero)
                        {
                            Debug.WriteLine("[AutoWood] Bilibili login successful");
                            return true; // 登录成功
                        }

                        return false; // 登录窗口仍然存在，继续尝试
                    }
                }

                return false; // 没有找到登录窗口
            }

            return false;
        }
        else
        {
            // Ignore and exit with OK
            return true;
        }
    }

    static (IntPtr windowHandle, string windowType) GetBiliLoginWindow(Process process)
    {
        IntPtr bHWnd = IntPtr.Zero;
        string windowType = "";

        User32.EnumWindows((hWnd, lParam) =>
        {
            try
            {
                // 获取窗口标题
                int titleLength = User32.GetWindowTextLength(hWnd);
                if (titleLength > 0)
                {
                    StringBuilder title = new StringBuilder(titleLength + 1);
                    User32.GetWindowText(hWnd, title, title.Capacity);

                    string titleText = title.ToString();

                    // 检查是否是B服登录窗口（通过标题匹配）
                    if (titleText.Contains("bilibili", StringComparison.OrdinalIgnoreCase))
                    {
                        // 检查窗口所有者是否是原神进程
                        var owner = User32.GetWindow(hWnd, User32.GetWindowCmd.GW_OWNER);
                        if (owner != IntPtr.Zero)
                        {
                            User32.GetWindowThreadProcessId(owner, out uint ownerPid);
                            if (ownerPid == process.Id)
                            {
                                // 检查窗口是否可见和启用
                                bool isVisible = User32.IsWindowVisible(hWnd);
                                bool isEnabled = User32.IsWindowEnabled(hWnd);

                                // 检查协议窗口
                                if (titleText.Contains("协议", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (isEnabled)
                                    {
                                        bHWnd = hWnd.DangerousGetHandle();
                                        windowType = "协议";
                                        return false;
                                    }
                                }

                                // 检查登录窗口
                                if (titleText.Contains("登录", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (isEnabled)
                                    {
                                        bHWnd = hWnd.DangerousGetHandle();
                                        windowType = "登录";
                                        return false;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AutoWood] 枚举窗口时出错: {ex.Message}");
            }

            return true;
        }, IntPtr.Zero);

        return (bHWnd, windowType);
    }

    private ImageRegion CaptureWindowToRectArea(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            throw new ArgumentException("无效的窗口句柄", nameof(hWnd));
        }

        // BitBlt 方式
        try
        {
            using var bitblt = GameCaptureFactory.Create(CaptureModes.BitBlt);
            bitblt.Start(hWnd, new Dictionary<string, object> { { "autoFixWin11BitBlt", true } });
            var img = GrabOneFrame(bitblt);
            if (img == null) throw new Exception("BitBlt 无帧");
            Debug.WriteLine($"[AutoWood] BitBlt 捕获窗口成功，尺寸 {img.Width}x{img.Height}");
            return BuildWindowClientRegion(hWnd, img);
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[AutoWood] BitBlt 捕获失败: {e.Message}");
        }
        
        // DwmSharedSurface方式
        try
        {
            using var dwm = GameCaptureFactory.Create(CaptureModes.DwmGetDxSharedSurface);
            dwm.Start(hWnd);
            var img = GrabOneFrame(dwm);
            if (img == null) throw new Exception("Dwm 无帧");
            Debug.WriteLine($"[AutoWood] DwmSharedSurface 捕获窗口成功，尺寸 {img.Width}x{img.Height}");
            return BuildWindowClientRegion(hWnd, img);
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[AutoWood] DwmSharedSurface 捕获失败，准备回退: {e.Message}");
        }

        // 全部失败，抛出异常由调用方回退到游戏截图
        throw new Exception("针对句柄的窗口截图失败");
    }
    
    private ImageRegion BuildWindowClientRegion(IntPtr hWnd, Mat img)
    {
        // 以窗口客户区的屏幕坐标为锚点，构造相对桌面的区域，使 Region.Click 映射到正确位置
        if (!User32.GetClientRect(hWnd, out var clientRect))
        {
            // 回退：用窗口矩形
            User32.GetWindowRect(hWnd, out var wr);
            return TaskContext.Instance().SystemInfo.DesktopRectArea.Derive(img, wr.left, wr.top);
        }
        POINT pt = default;
        User32.ClientToScreen(hWnd, ref pt);
        return TaskContext.Instance().SystemInfo.DesktopRectArea.Derive(img, pt.X, pt.Y);
    }

    private Mat? GrabOneFrame(IGameCapture capture, int retry = 8, int delayMs = 40)
    {
        for (int i = 0; i < retry; i++)
        {
            var img = capture.Capture();
            if (img != null)
            {
                return img;
            }
            Thread.Sleep(delayMs);
        }
        return null;
    }

    /// <summary>
    /// 将 Region 中心点映射到 1080P 坐标系并点击。
    /// </summary>
    private void ClickRegionCenterBy1080(Region region)
    {
        // 计算区域中心的桌面坐标
        var (centerDesktopX, centerDesktopY) = region.ConvertPositionToDesktopRegion(region.Width / 2, region.Height / 2);

        // 转换到游戏捕获区域坐标
        var captureRect = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var inCaptureX = centerDesktopX - captureRect.X;
        var inCaptureY = centerDesktopY - captureRect.Y;
        if (inCaptureX < 0 || inCaptureY < 0)
        {
            // 不在游戏捕获区域内，直接回退为桌面点击
            DesktopRegion.DesktopRegionClick(centerDesktopX, centerDesktopY);
            return;
        }

        // 映射为 1080P 坐标
        var scale = TaskContext.Instance().SystemInfo.ScaleTo1080PRatio;
        var x1080 = inCaptureX / scale;
        var y1080 = inCaptureY / scale;
        GameCaptureRegion.GameRegion1080PPosClick(x1080, y1080);
    }
}
