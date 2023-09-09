using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask
{
    public class SystemControl
    {
        public static IntPtr FindGenshinImpactHandle()
        {
            return FindHandleByProcessName("YuanShen", "GenshinImpact", "Genshin Impact");
        }

        public static bool IsGenshinImpactActive()
        {
            var name = GetActiveProcessName();
            return name is "YuanShen" or "GenshinImpact" or "Genshin Impact";
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
    }
}