using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask
{
    public class SystemControl
    {
        public static IntPtr FindGenshinImpactHandle()
        {
            return FindHandleByProcessName("YuanShen", "GenshinImpact", "Genshin Impact Cloud Game");
        }

        public static bool IsGenshinImpactActive()
        {
            var name = GetActiveProcessName();
            return name is "YuanShen" or "GenshinImpact" or "Genshin Impact Cloud Game";
        }

        public static IntPtr FindHandleByProcessName(params string[] names)
        {
            foreach (var name in names)
            {
                var pros = Process.GetProcessesByName(name);
                if (pros.Any())
                {
                    return pros[0].MainWindowHandle;
                }
            }

            return IntPtr.Zero;
        }

        public static string? GetActiveProcessName()
        {
            try
            {
                var hWnd = User32.GetForegroundWindow();
                User32.GetWindowThreadProcessId(hWnd, out var pid);
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
        public static RECT GetWindowRect(IntPtr hWnd)
        {
            User32.GetWindowRect(hWnd, out var windowRect);
            return windowRect;
        }

        /// <summary>
        /// 游戏本身分辨率获取
        /// </summary>
        /// <param name="hWnd"></param>
        /// <returns></returns>
        public static RECT GetGameScreenRect(IntPtr hWnd)
        {
            User32.GetClientRect(hWnd, out var clientRect);
            return clientRect;
        }

        //public static int GetCaptionHeight()
        //{
        //    return User32.GetSystemMetrics(User32.SystemMetric.SM_CYFRAME) + User32.GetSystemMetrics(User32.SystemMetric.SM_CYCAPTION);
        //}
    }
}