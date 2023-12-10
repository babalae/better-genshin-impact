using BetterGenshinImpact.GameTask.Model;
using System;
using System.Diagnostics;
using BetterGenshinImpact.Helpers;
using Vanara.PInvoke;
using System.Windows.Interop;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.View;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Genshin.Settings;

namespace BetterGenshinImpact.GameTask
{
    /// <summary>
    /// 任务上下文
    /// </summary>
    public class TaskContext
    {

        private static TaskContext? _uniqueInstance;
        private static readonly object Locker = new();

        private TaskContext()
        {
        }

        public static TaskContext Instance()
        {
            if (_uniqueInstance == null)
            {
                lock (Locker)
                {
                    _uniqueInstance ??= new TaskContext();
                }
            }
            return _uniqueInstance;
        }

        public void Init(IntPtr hWnd)
        {
            GameHandle = hWnd;
            SystemInfo = new SystemInfo(hWnd);
            DpiScale = DpiHelper.ScaleY;
            //MaskWindowHandle = new WindowInteropHelper(MaskWindow.Instance()).Handle;
            IsInitialized = true;
        }

        public bool IsInitialized { get; set; }

        public IntPtr GameHandle { get; set; }

        //public IntPtr MaskWindowHandle { get; set; }

        public float DpiScale { get; set; }


        public SystemInfo SystemInfo { get; set; }


        public AllConfig Config
        {
            get
            {
                if (ConfigService.Config == null)
                {
                    throw new Exception("Config未初始化");
                }
                return ConfigService.Config;
            }
        }

        public SettingsContainer? GameSettings { get; set; }
    }
}
