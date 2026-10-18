using BetterGenshinImpact.Core.Input.Backends.Win32;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Runtime;
using Fischless.GameCapture;
using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.UnitTest.GameTaskTests
{
    /// <summary>
    /// 为单元测试构造不连接真实游戏的运行环境：窗口恒为存活、不依赖前台，截图器不产出画面
    /// </summary>
    internal static class TestGameRuntime
    {
        /// <summary>
        /// 用指定的画面信息构造运行环境
        /// </summary>
        /// <param name="systemInfo">测试使用的画面信息，例如 <see cref="FakeSystemInfo"/></param>
        public static GameRuntime Create(ISystemInfo systemInfo)
        {
            return new GameRuntime(
                GameRuntimeKind.Win32Window,
                new FakeGameWindow(systemInfo),
                new FakeGameCapture(),
                new Win32InputBackend(IntPtr.Zero),
                systemInfo,
                NullMaskWindowDrawingBoard.Instance,
                new MaskWindowMapState());
        }

        /// <summary>
        /// 测试用游戏窗口：画面区域取自画面信息，不监听窗口事件
        /// </summary>
        private sealed class FakeGameWindow(ISystemInfo systemInfo) : IGameWindow
        {
            /// <summary>
            /// 没有真实窗口
            /// </summary>
            public nint Handle => 0;

            /// <summary>
            /// 当前测试进程
            /// </summary>
            public int ProcessId => Environment.ProcessId;

            /// <summary>
            /// 画面区域与画面信息的捕获区域一致
            /// </summary>
            public GameViewport Viewport => new(systemInfo.CaptureAreaRect, systemInfo.DpiScale);

            /// <summary>
            /// 恒为存活
            /// </summary>
            public bool IsAlive => true;

            /// <summary>
            /// 恒在前台
            /// </summary>
            public bool IsForeground => true;

            /// <summary>
            /// 恒未最小化
            /// </summary>
            public bool IsMinimized => false;

            /// <summary>
            /// 不依赖前台，等待时不做焦点恢复
            /// </summary>
            public bool RequiresForeground => false;

            /// <summary>
            /// 无操作
            /// </summary>
            public void Activate()
            {
            }

            /// <summary>
            /// 画面区域不会变化，不触发事件
            /// </summary>
            public event EventHandler? ViewportChanged
            {
                add { }
                remove { }
            }

            /// <summary>
            /// 无需释放
            /// </summary>
            public void Dispose()
            {
            }
        }

        /// <summary>
        /// 测试用截图器：不产出画面，测试直接构造截图区域
        /// </summary>
        private sealed class FakeGameCapture : IGameCapture
        {
            /// <summary>
            /// 恒为运行中
            /// </summary>
            public bool IsCapturing => true;

            /// <summary>
            /// 无操作
            /// </summary>
            public void Start(nint hWnd, Dictionary<string, object>? settings = null)
            {
            }

            /// <summary>
            /// 不产出画面
            /// </summary>
            public GameCaptureFrame? Capture() => null;

            /// <summary>
            /// 无操作
            /// </summary>
            public void Stop()
            {
            }

            /// <summary>
            /// 无需释放
            /// </summary>
            public void Dispose()
            {
            }
        }
    }
}
