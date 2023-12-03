using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Helpers.Extensions;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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
                                    if (kv.StartsWith("cps=") && kv.EndsWith("bilibili"))
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

    public void Login(CancellationTokenSource cts)
    {
        int failCount = default;

        while (!LoginPrivate(cts))
        {
            // It is necessary to support exitable trying.
            // Can exit trying when over than 10s.
            if (++failCount > 20)
            {
                Debug.WriteLine("[AutoWood] Give up to check login button and don't try again.");
                break;
            }
            Debug.WriteLine($"[AutoWood] Fail to check login button {failCount} time(s).");
            Sleep(500, cts);
        }
        Debug.WriteLine("[AutoWood] Exit while check login button.");
    }

    private bool LoginPrivate(CancellationTokenSource cts)
    {
        if (Type == The3rdPartyType.Bilibili)
        {
            if (Process.GetProcessesByName("YuanShen").FirstOrDefault() is Process process)
            {
                static nint GetBHWnd(Process process)
                {
                    nint bHWnd = IntPtr.Zero;

                    _ = User32.EnumWindows((HWND hWnd, nint lParam) =>
                    {
                        try
                        {
                            _ = User32.GetWindowThreadProcessId(hWnd, out uint pid);

                            if (pid == process.Id)
                            {
                                int capacity = User32.GetWindowTextLength(hWnd);
                                StringBuilder title = new(capacity + 1);
                                _ = User32.GetWindowText(hWnd, title, title.Capacity);

                                Debug.WriteLine($"[AutoWood] Enum Windows result is {title}");
                                if (title.ToString().Contains("bilibili", StringComparison.OrdinalIgnoreCase))
                                {
                                    bHWnd = (nint)hWnd;
                                    return false;
                                }
                            }
                        }
                        catch
                        {
                            ///
                        }
                        return true;
                    }, IntPtr.Zero);

                    return bHWnd;
                }

                if (GetBHWnd(process) != IntPtr.Zero)
                {
                    // Just for login WebUI fadein chattering
                    Sleep(4000, cts);

                    var p = TaskContext.Instance()
                        .SystemInfo.CaptureAreaRect.GetCenterPoint()
                        .Add(new(0, 125));

                    p.Click();
                    Debug.WriteLine("[AutoWood] Click login button for the B one");

                    // Just for login WebUI fadeout chattering
                    Sleep(3000, cts);

                    if (GetBHWnd(process) != IntPtr.Zero)
                    {
                        p.Click();
                        Debug.WriteLine("[AutoWood] Click login button for the B one [LAST CHANCE]");
                    }

                    return true;
                }
                return false;
            }
            return false;
        }
        else
        {
            // Ignore and exit with OK
            return true;
        }
    }
}
