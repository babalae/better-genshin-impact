using BetterGenshinImpact.GameTask.Model;
using System;
using BetterGenshinImpact.Helpers;
using Vanara.PInvoke;

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
        }

        public IntPtr GameHandle { get; set; }

        public float DpiScale;


        public SystemInfo SystemInfo { get; set; }
    }
}
