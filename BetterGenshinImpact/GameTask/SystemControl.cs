using System;
using System.Diagnostics;
using System.Linq;
using Windows.Win32.Foundation;
using static Windows.Win32.PInvoke;

namespace BetterGenshinImpact.GameTask
{
    public class SystemControl
    {
        public static nint FindGenshinImpactHandle()
        {
            return FindHandleByProcessName("YuanShen", "GenshinImpact", "Genshin Impact Cloud Game");
        }

        public static bool IsGenshinImpactActive()
        {
            var name = GetActiveProcessName();
            return name is "YuanShen" or "GenshinImpact" or "Genshin Impact Cloud Game";
        }

        public static nint FindHandleByProcessName(params string[] names)
        {
            foreach (var name in names)
            {
                var pros = Process.GetProcessesByName(name);
                if (pros.Any())
                {
                    return pros[0].MainWindowHandle;
                }
            }

            return 0;
        }

        public static unsafe string? GetActiveProcessName()
        {
            try
            {
                var hWnd = GetForegroundWindow();
                var pid = GetWindowThreadProcessId(hWnd);
                var p = Process.GetProcessById((int)pid);
                return p.ProcessName;
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// 获取窗口位置
        /// </summary>
        /// <param name="hWnd"></param>
        /// <returns></returns>
        public static RECT GetWindowRect(HWND hWnd)
        {
            Windows.Win32.PInvoke.GetWindowRect(hWnd, out var windowRect);
            return windowRect;
        }

        /// <summary>
        /// 游戏本身分辨率获取
        /// </summary>
        /// <param name="hWnd"></param>
        /// <returns></returns>
        public static RECT GetGameScreenRect(HWND hWnd)
        {
            Windows.Win32.PInvoke.GetWindowRect(hWnd, out var clientRect);
            return clientRect;
        }

        //public static int GetCaptionHeight()
        //{
        //    return User32.GetSystemMetrics(User32.SystemMetric.SM_CYFRAME) + User32.GetSystemMetrics(User32.SystemMetric.SM_CYCAPTION);
        //}
    }
}