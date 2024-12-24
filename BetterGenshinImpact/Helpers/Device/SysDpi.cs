using System;
using System.Diagnostics;
using System.Windows.Forms;
using System.Windows.Interop;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Model;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Helpers.Device;

public class SysDpi : Singleton<SysDpi>
{
    public string? originalDpiRate;

    // 显示器编号
    public string ScreenIndex { get; set; } = "1";

    private bool needResetDpi = false;

    public SysDpi()
    {
        // 获取当前WPF窗口的句柄
        var windowHandle = new WindowInteropHelper(UIDispatcherHelper.MainWindow).Handle;
        // 使用窗口句柄获取显示器信息
        var screen = Screen.FromHandle(windowHandle);
        // 获取当前显示器的编号
        int screenIndex = GetScreenIndex(screen);
        ScreenIndex = (screenIndex + 1).ToString();
    }


    public void SetDpi()
    {
        try
        {
            originalDpiRate = RunSetDpiExe($"value {ScreenIndex}");
            if (originalDpiRate == "100")
            {
                TaskControl.Logger.LogError("当前DPI已经是100，无需设置");
                return;
            }

            RunSetDpiExe($"100 {ScreenIndex}"); // 设置DPI为100
            needResetDpi = true;
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogError(e, "设置DPI时出错");
        }
    }

    public void ResetDpi()
    {
        try
        {
            if (originalDpiRate != null && needResetDpi)
            {
                RunSetDpiExe($"{originalDpiRate} {ScreenIndex}");
            }
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogError(e, "重新设置DPI时出错");
        }
    }

    public string? RunSetDpiExe(string arguments)
    {
        try
        {
            string exePath = Global.Absolute(@"video\bin\SetDPI.exe");
            // 创建一个新的进程启动信息
            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // 启动进程
            using Process? process = Process.Start(processStartInfo);
            // 读取输出
            string? output = process?.StandardOutput.ReadToEnd();
            process?.WaitForExit();
            return output;
        }
        catch (Exception ex)
        {
            TaskControl.Logger.LogError(ex, $"调用 SetDPI.exe 时出错: {ex.Message}");
            return null;
        }
    }


    private int GetScreenIndex(Screen screen)
    {
        // 获取所有显示器的列表
        var screens = Screen.AllScreens;

        // 查找当前显示器在列表中的索引
        for (int i = 0; i < screens.Length; i++)
        {
            if (screens[i].DeviceName == screen.DeviceName)
            {
                return i;
            }
        }

        // 如果未找到匹配的显示器，返回-1
        return -1;
    }
}