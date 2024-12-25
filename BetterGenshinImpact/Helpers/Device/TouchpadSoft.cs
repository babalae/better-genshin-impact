using System;
using System.Runtime.InteropServices;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Helpers.Device;

public class TouchpadSoft : Singleton<TouchpadSoft>
{
    private const string TouchpadRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\Status";
    private const string TouchpadRegistryKey = "Enabled";
    private int? previousStatus = null;

    // 获取触控板状态的方法
    private int GetTouchpadStatus()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(TouchpadRegistryPath, true))
        {
            if (key != null)
            {
                object value = key.GetValue(TouchpadRegistryKey);
                if (value != null)
                {
                    return (int)value;
                }
                else
                {
                    // 如果键值不存在，则写入默认值1（启用触控板）
                    key.SetValue(TouchpadRegistryKey, 1, RegistryValueKind.DWord);
                    return 1;
                }
            }
            else
            {
                throw new InvalidOperationException("Registry path not found.");
            }
        }
    }

    // 设置触控板状态的方法
    private void SetTouchpadStatus(int status)
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(TouchpadRegistryPath, true))
        {
            if (key != null)
            {
                key.SetValue(TouchpadRegistryKey, status, RegistryValueKind.DWord);
            }
            else
            {
                throw new InvalidOperationException("Registry path not found.");
            }
        }
    }

    // 只查询触控板状态的方法
    public int QueryTouchpadStatus()
    {
        return GetTouchpadStatus();
    }

    // 检查并记录触控板状态的方法
    public void CheckAndRecordStatus()
    {
        previousStatus = GetTouchpadStatus();
        Console.WriteLine("Current Touchpad Status: " + previousStatus);
    }

    // 关闭触控板的方法
    public void DisableTouchpad()
    {
        SetTouchpadStatus(0);
        Console.WriteLine("Touchpad has been disabled.");
    }

    // 还原触控板状态的方法
    public void RestoreTouchpad()
    {
        if (previousStatus.HasValue)
        {
            SetTouchpadStatus(previousStatus.Value);
            Console.WriteLine("Touchpad status has been restored to: " + previousStatus.Value);
        }
        else
        {
            Console.WriteLine("No previous status recorded.");
        }
    }

    [Flags]
    public enum SendMessageTimeoutFlags : uint
    {
        SMTO_NORMAL = 0x0,
        SMTO_BLOCK = 0x1,
        SMTO_ABORTIFHUNG = 0x2,
        SMTO_NOTIMEOUTIFNOTHUNG = 0x8
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, IntPtr lParam,
        SendMessageTimeoutFlags fuFlags, uint uTimeout, out UIntPtr lpdwResult);


    IntPtr HWND_BROADCAST = new IntPtr(0xffff);
    const uint WM_SETTINGCHANGE = 0x1A;
    const int MSG_TIMEOUT = 15000;
    UIntPtr RESULT;
    string ENVIRONMENT = "Environment";

    public void NotifySettingChange()
    {
        SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, (IntPtr)Marshal.StringToHGlobalAnsi(ENVIRONMENT), SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, MSG_TIMEOUT, out RESULT);
    }

    public void SwitchTouchpadByHotKey()
    {
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_LCONTROL);
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_LWIN);
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_F24);
        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_F24);
        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_LWIN);
        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_LCONTROL);
    }

    public void DisableTouchpadWhenEnabledByHotKey()
    {
        if (previousStatus == 1)
        {
            SwitchTouchpadByHotKey();
        }
    }
    
    public void RestoreTouchpadByHotKey()
    {
        if (previousStatus == 1)
        {
            SwitchTouchpadByHotKey();
        }
    }
}