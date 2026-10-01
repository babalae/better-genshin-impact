using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Service;
using System;
using System.Threading;

namespace BetterGenshinImpact.GameTask
{
    /// <summary>
    /// 任务上下文：当前游戏运行环境的只读视图，以及配置入口。
    /// <para>
    /// 运行环境由 <see cref="GameRuntimeService"/> 绑定和解绑，其他代码只读。
    /// 新代码请直接使用 <see cref="Runtime"/>，或注入 <see cref="GameRuntimeService"/>。
    /// </para>
    /// </summary>
    public class TaskContext
    {
        private static TaskContext? _uniqueInstance;
        private static object? InstanceLocker;

#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑声明为可以为 null。

        private TaskContext()
        {
        }

#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑声明为可以为 null。

        public static TaskContext Instance()
        {
            return LazyInitializer.EnsureInitialized(ref _uniqueInstance, ref InstanceLocker, () => new TaskContext());
        }

        /// <summary>
        /// 当前运行环境。截图器未启动时为 null
        /// </summary>
        public GameRuntime? Runtime { get; private set; }

        public bool IsInitialized => Runtime is not null;

        /// <summary>
        /// 最近一次绑定的游戏窗口句柄。解绑后保留最后一次的值，与改造前一致。
        /// 兼容旧调用点，新代码请使用 Runtime.Window
        /// </summary>
        public IntPtr GameHandle { get; private set; }

        /// <summary>
        /// 绑定时记录的 DPI 快照。解绑后保留最后一次的值
        /// </summary>
        public float DpiScale { get; private set; }

        /// <summary>
        /// 绑定时由画面区域构建。解绑后保留最后一次的值
        /// </summary>
        public ISystemInfo SystemInfo { get; private set; }

        /// <summary>
        /// 只是转发 ConfigService.Config，与运行环境无关。新代码请注入 IConfigService
        /// </summary>
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

        /// <summary>
        /// 绑定或解绑运行环境，只由 <see cref="GameRuntimeService"/> 调用。
        /// 画面分辨率不合规时抛出异常，此时状态保持不变
        /// </summary>
        internal void Bind(GameRuntime? runtime)
        {
            if (runtime is not null)
            {
                var viewport = runtime.Window.Viewport;
                SystemInfo = new SystemInfo(viewport, runtime.MaskWindowDrawingBoard);
                DpiScale = viewport.DpiScale;
                GameHandle = runtime.Window.Handle;
            }

            Runtime = runtime;
        }
    }
}
