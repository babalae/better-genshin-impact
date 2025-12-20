using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Platform.Wine
{
    /// <summary>
    /// Wine/Linux 兼容性增强插件
    /// 包含：全局环境检测、启动配置、定时轮询工具
    /// </summary>
    public class WinePlatformAddon : IDisposable
    {
        // =============================================================
        //  Part 1: 静态成员 (全局检测、配置、工具方法)
        // =============================================================

        private static bool? _isWineCached;

        /// <summary>
        /// [全局静态] 检查是否运行在 Wine 环境中
        /// </summary>
        public static bool IsRunningOnWine
        {
            get
            {
                _isWineCached ??= CheckIsRunningOnWine();
                return _isWineCached.Value;
            }
        }

        /// <summary>
        /// [全局静态] 应用程序启动初始化配置 (App.xaml.cs 调用)
        /// </summary>
        public static void ApplyApplicationConfig()
        {
            if (IsRunningOnWine)
            {
                // 强制使用软件渲染，解决 WPF 硬件加速在 Wine 下的黑屏/崩溃问题
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                System.Diagnostics.Debug.WriteLine("【兼容层】Wine环境检测：已启用软件渲染模式。");
            }
        }

        /// <summary>
        /// [全局静态] 检查物理按键状态 (封装 Win32 API)
        /// </summary>
        public static bool IsKeyDown(Keys key)
        {
            // 最高位为 1 表示按下
            return (GetAsyncKeyState((int)key) & 0x8000) != 0;
        }

        // =============================================================
        //  Part 2: 实例成员 (定时器生命周期管理)
        // =============================================================

        private readonly System.Timers.Timer _pollingTimer;
        private Action? _pollingAction;
        private readonly ILogger? _logger;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">可选日志记录器</param>
        public WinePlatformAddon(ILogger? logger = null)
        {
            _logger = logger;
            _pollingTimer = new System.Timers.Timer();
            _pollingTimer.Elapsed += OnTimerElapsed;
            // 默认为 AutoReset = true (循环触发)
        }

        /// <summary>
        /// 启动定时任务 (例如：位置同步、按键轮询)
        /// </summary>
        /// <param name="action">每帧执行的回调</param>
        /// <param name="intervalMs">间隔毫秒数</param>
        public void StartPolling(Action action, int intervalMs)
        {
            if (!IsRunningOnWine)
                return; // Windows 环境下直接忽略，不启动定时器

            _pollingAction = action;
            _pollingTimer.Interval = intervalMs;

            if (!_pollingTimer.Enabled)
            {
                _pollingTimer.Start();
                _logger?.LogInformation($"【兼容层】已启动轮询定时器 (Interval: {intervalMs}ms)");
            }
        }

        /// <summary>
        /// 停止定时任务
        /// </summary>
        public void StopPolling()
        {
            if (_pollingTimer.Enabled)
            {
                _pollingTimer.Stop();
            }
        }

        private void OnTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                _pollingAction?.Invoke();
            }
            catch (Exception ex)
            {
                // 避免轮询线程崩溃影响主程序
                _logger?.LogDebug(ex, "WinePlatformAddon Polling Error");
            }
        }

        public void Dispose()
        {
            StopPolling();
            _pollingTimer.Dispose();
        }

        // =============================================================
        //  Part 3: 内部 P/Invoke (对外隐藏)
        // =============================================================

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Ansi,
            ExactSpelling = true,
            SetLastError = true
        )]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static bool CheckIsRunningOnWine()
        {
            try
            {
                IntPtr hModule = GetModuleHandle("ntdll.dll");
                if (hModule == IntPtr.Zero)
                    return false;
                IntPtr fPtr = GetProcAddress(hModule, "wine_get_version");
                return fPtr != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }
    }
}
