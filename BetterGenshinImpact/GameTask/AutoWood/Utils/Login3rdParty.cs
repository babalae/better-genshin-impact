using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BetterGenshinImpact.GameTask.Model.Area;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

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
                // B服登录
                var (loginWindow, windowType) = GetBiliLoginWindow(process);
                if (loginWindow != IntPtr.Zero)
                {
                    if (windowType.Contains("协议"))
                    {
                        GameCaptureRegion.GameRegion1080PPosClick(1030, 615);

                        // 检查窗口是否还存在
                        var (remainingWindow, remainingType) = GetBiliLoginWindow(process);
                        if (remainingWindow == IntPtr.Zero || !remainingType.Contains("协议"))
                        {
                            // 协议窗口已消失，继续等待登录窗口
                            return false;
                        }
                        // 协议窗口仍然存在，继续尝试
                        return false;
                    }
                    if (windowType.Contains("登录"))
                    {
                        Thread.Sleep(2000);
                        GameCaptureRegion.GameRegion1080PPosClick(960, 630);
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
}
