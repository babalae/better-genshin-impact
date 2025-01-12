using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Helpers.Device;

public class StickyKeysManager
{
    // 导入需要的 Windows API
    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, 
        ref STICKYKEYS pvParam, uint fWinIni);

    // STICKYKEYS 结构体
    [StructLayout(LayoutKind.Sequential)]
    private struct STICKYKEYS
    {
        public uint cbSize;
        public uint dwFlags;
    }

    // 常量定义
    private const uint SKF_STICKYKEYSON = 0x00000001;
    

    // 常量定义
    private const uint SPI_GETSTICKYKEYS = 0x003A;
    private const uint SPI_SETSTICKYKEYS = 0x003B;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;
    
    // 粘滞键标志
    private const uint SKF_HOTKEYACTIVE = 0x00000004;      // 热键是否激活
    private const uint SKF_CONFIRMHOTKEY = 0x00000008;     // 是否显示确认对话框
    private const uint SKF_HOTKEYSOUND = 0x00000010;       // 是否播放声音
    private const uint SKF_INDICATOR = 0x00000020;         // 是否显示图标

    public static void DisableStickyKeysShortcut()
    {
        try
        {
            // 创建 STICKYKEYS 结构体
            STICKYKEYS stickyKeys = new STICKYKEYS();
            stickyKeys.cbSize = (uint)Marshal.SizeOf(typeof(STICKYKEYS));

            // 获取当前设置
            SystemParametersInfo(SPI_GETSTICKYKEYS, 0, ref stickyKeys, 0);

            
            // 禁用粘滞键
            stickyKeys.dwFlags &= ~SKF_STICKYKEYSON;

            // 禁用所有提示和快捷方式相关的标志
            stickyKeys.dwFlags &= ~SKF_HOTKEYACTIVE;    // 禁用热键
            stickyKeys.dwFlags &= ~SKF_CONFIRMHOTKEY;   // 禁用确认对话框
            stickyKeys.dwFlags &= ~SKF_HOTKEYSOUND;     // 禁用声音提示
            stickyKeys.dwFlags &= ~SKF_INDICATOR;       // 禁用图标显示

            // 应用新设置
            SystemParametersInfo(SPI_SETSTICKYKEYS, 0, ref stickyKeys, 
                SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

            Console.WriteLine("粘滞键快捷方式已成功禁用！");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"禁用粘滞键快捷方式时出错: {ex.Message}");
        }
    }

    public static void EnableStickyKeysShortcut()
    {
        try
        {
            // 创建 STICKYKEYS 结构体
            STICKYKEYS stickyKeys = new STICKYKEYS();
            stickyKeys.cbSize = (uint)Marshal.SizeOf(typeof(STICKYKEYS));

            // 获取当前设置
            SystemParametersInfo(SPI_GETSTICKYKEYS, 0, ref stickyKeys, 0);

            
            // 启用粘滞键
            stickyKeys.dwFlags |= SKF_STICKYKEYSON;
            // 启用所有提示和快捷方式相关的标志
            stickyKeys.dwFlags |= SKF_HOTKEYACTIVE;    // 启用热键
            stickyKeys.dwFlags |= SKF_CONFIRMHOTKEY;   // 启用确认对话框
            stickyKeys.dwFlags |= SKF_HOTKEYSOUND;     // 启用声音提示
            stickyKeys.dwFlags |= SKF_INDICATOR;       // 启用图标显示

            // 应用新设置
            SystemParametersInfo(SPI_SETSTICKYKEYS, 0, ref stickyKeys, 
                SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

            Console.WriteLine("粘滞键快捷方式已成功启用！");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"启用粘滞键快捷方式时出错: {ex.Message}");
        }
    }
}