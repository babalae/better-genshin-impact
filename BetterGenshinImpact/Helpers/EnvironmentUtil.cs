using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Helpers;

public class EnvironmentUtil
{
    public static bool IsProcessRunning(string processName)
    {
        // // 检测 QQ
        // bool isQQRunning = IsProcessRunning("QQ"); // QQ的进程名称可能需要确认
        // Console.WriteLine("QQ is " + (isQQRunning ? "running" : "not running"));
        //
        // // 检测微信
        // bool isWeChatRunning = IsProcessRunning("WeChat"); // WeChat的进程名称可能需要确认
        // Console.WriteLine("WeChat is " + (isWeChatRunning ? "running" : "not running"));
        //
        // // 检测飞书
        // bool isFeiShuRunning = IsProcessRunning("FeiShu"); // FeiShu的进程名称可能需要确认
        // Console.WriteLine("FeiShu is " + (isFeiShuRunning ? "running" : "not running"));
        // 获取所有运行中的进程
        Process[] processes = Process.GetProcessesByName(processName);
        return processes.Length > 0;
    }

    public static DateTime? LastBootUpTime()
    {
        try
        {
            // 创建管理对象查询
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem");

            foreach (ManagementObject queryObj in searcher.Get())
            {
                // 获取系统开机时间
                DateTime bootTime = ManagementDateTimeConverter.ToDateTime(queryObj["LastBootUpTime"].ToString());
                Debug.WriteLine("系统开机时间: " + bootTime);
                return bootTime;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("An error occurred: " + ex.Message);
        }

        return null;
    }

    public static uint GetMouseSpeed()
    {
        User32.SystemParametersInfo(User32.SPI.SPI_GETMOUSESPEED, out uint mouseSpeed);
        return mouseSpeed;
    }

    // 设置鼠标速度
    public static void SetMouseSpeed(uint speed)
    {
        User32.SystemParametersInfo(User32.SPI.SPI_SETMOUSESPEED, speed);
    }

    //SPI_GETWHEELSCROLLLINES
    public static uint GetWheelScrollLines()
    {
        User32.SystemParametersInfo(User32.SPI.SPI_GETWHEELSCROLLLINES, out uint wheelScrollLines);
        return wheelScrollLines;
    }

    // 设置鼠标滚轮滚动行数
    public static void SetWheelScrollLines(uint lines)
    {
        User32.SystemParametersInfo(User32.SPI.SPI_SETWHEELSCROLLLINES, lines);
    }

    // SPI_GETMOUSETRAILS
    public static uint GetMouseTrails()
    {
        User32.SystemParametersInfo(User32.SPI.SPI_GETMOUSETRAILS, out uint mouseTrails);
        return mouseTrails;
    }

    // 设置鼠标拖尾
    public static void SetMouseTrails(uint trails)
    {
        User32.SystemParametersInfo(User32.SPI.SPI_SETMOUSETRAILS, trails);
    }

    // SPI_GETMOUSE
    /// <summary>
    /// https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-mouse_event#remarks
    /// </summary>
    /// <returns></returns>
    public static (int, int, int) GetMouse()
    {
        int[] mouseParams = new int[3];
        IntPtr ptr = Marshal.AllocHGlobal(sizeof(int) * mouseParams.Length);
        try
        {
            User32.SystemParametersInfo(User32.SPI.SPI_GETMOUSE, (uint)mouseParams.Length, ptr, User32.SPIF.None);
            Marshal.Copy(ptr, mouseParams, 0, mouseParams.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        return (mouseParams[0], mouseParams[1], mouseParams[2]);
    }

    public static void PrintMouseSettings()
    {
        Debug.WriteLine("鼠标速度: " + GetMouseSpeed());
        Debug.WriteLine("鼠标滚轮滚动行数: " + GetWheelScrollLines());
        Debug.WriteLine("鼠标拖尾: " + GetMouseTrails());
        var (mouseThreshold1, mouseThreshold2, mouseThreshold3) = GetMouse();
        Debug.WriteLine("鼠标阈值1 Mouse Double Click Time (ms): " + mouseThreshold1);
        Debug.WriteLine("鼠标阈值2 Mouse Buttons Swapped:: " + mouseThreshold2);
        Debug.WriteLine("鼠标阈值3 Mouse Speed:  " + mouseThreshold3);

        // Debug.WriteLine("Touchpad Enabled: " + IsTouchpadEnabled());

        Debug.WriteLine("当前音量: " + GetMasterVolume());
    }

    public static bool IsTouchpadEnabled()
    {
        try
        {
            // Create a ManagementObjectSearcher to query for touchpad devices
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PointingDevice");

            foreach (ManagementObject obj in searcher.Get())
            {
                // Check if the device is a touchpad
                if (obj["Description"].ToString().ToLower().Contains("touchpad"))
                {
                    // Get the status of the touchpad
                    string status = obj["Status"].ToString();

                    Debug.WriteLine($"Touchpad Status: {status}");

                    // Check if the touchpad is enabled
                    if (status.Equals("OK", StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine("The touchpad is enabled.");
                        return true;
                    }
                    else
                    {
                        Debug.WriteLine("The touchpad is disabled.");
                        return false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"An error occurred: {ex.Message}");
        }

        return true;
    }

    public static List<(string, int)> GetCurrentSpeakerVolume()
    {
        int volume = 0;
        var enumerator = new MMDeviceEnumerator();

        //获取音频输出设备
        IEnumerable<MMDevice> speakDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToArray();

        List<(string, int)> devices = new();
        foreach (var speakDevice in speakDevices)
        {
            Debug.WriteLine($"Device Friendly Name: {speakDevice.FriendlyName}");
            Debug.WriteLine($"Device Volume: {speakDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100}");
            volume = (int)(speakDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
            devices.Add((speakDevice.FriendlyName, volume));
        }

        return devices;
    }

    public static float GetMasterVolume()
    {
        var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return device.AudioEndpointVolume.MasterVolumeLevelScalar;
    }

    public static void GetCurrentSpeakerVolume(int volume)
    {
        var enumerator = new MMDeviceEnumerator();
        IEnumerable<MMDevice> speakDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToArray();
        if (speakDevices.Count() > 0)
        {
            MMDevice mMDevice = speakDevices.ToList()[0];
            mMDevice.AudioEndpointVolume.MasterVolumeLevelScalar = volume / 100.0f;
        }
    }
}