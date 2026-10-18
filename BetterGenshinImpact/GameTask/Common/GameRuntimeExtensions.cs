using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using Size = OpenCvSharp.Size;

namespace BetterGenshinImpact.GameTask.Common;

/// <summary>
/// 绑定到某个运行环境的任务操作：截图、等待（含暂停与焦点恢复）、按 1080P 坐标点击。
/// 只认传入的这个运行环境，不查询"当前"：运行环境停止后按正常中断处理，不会误操作新绑定的环境
/// </summary>
public static class GameRuntimeExtensions
{
    /// <summary>
    /// 任务公共日志
    /// </summary>
    private static ILogger Logger => TaskControl.Logger;

    /// <summary>
    /// 配置根。扩展方法没有创建方可以传参，暂时读静态入口（见 task-context-removal.md 第 5 节）
    /// </summary>
    private static AllConfig Config => ConfigService.Config!;

    #region 截图

    /// <summary>
    /// 截取一帧并派生为 1080P 区域树（大于 1080P 时缩放）。运行环境已停止时按正常中断处理
    /// </summary>
    /// <exception cref="NormalEndException">运行环境已停止</exception>
    public static ImageRegion CaptureToRectArea(this GameRuntime runtime)
    {
        ThrowIfStopping(runtime);
        OpenCvSharp.Mat image;
        try
        {
            image = TaskControl.CaptureGameImage(runtime.Capture);
        }
        catch (Exception) when (runtime.IsStopping)
        {
            // 截图期间运行环境被停止并释放：与停止前的检查一致，按正常中断处理
            throw new NormalEndException("游戏运行环境已停止");
        }

        return new CaptureContent(image, 0, 0, runtime.SystemInfo).CaptureRectArea;
    }

    #endregion

    #region 等待

    /// <summary>
    /// 检查暂停与游戏焦点后等待。运行环境停止时中断
    /// </summary>
    public static void CheckAndSleep(this GameRuntime runtime, int millisecondsTimeout)
    {
        var wait = CreateWait(runtime, CancellationToken.None);
        TrySuspend(wait);
        CheckAndActivateGameWindow(runtime, wait);

        wait.Sleep(millisecondsTimeout);
    }

    /// <summary>
    /// 重试焦点检查后等待，并在等待期间检查运行环境是否已停止
    /// </summary>
    public static void Sleep(this GameRuntime runtime, int millisecondsTimeout)
    {
        var wait = CreateWait(runtime, CancellationToken.None);
        NewRetry.Do(() =>
        {
            TrySuspend(wait);
            CheckAndActivateGameWindow(runtime, wait);
        }, TimeSpan.FromSeconds(1), 100);
        wait.Sleep(millisecondsTimeout);
    }

    /// <summary>
    /// 同步等待，并把取消令牌传入暂停、焦点恢复和延时内部
    /// </summary>
    public static void Sleep(this GameRuntime runtime, int millisecondsTimeout, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            throw new NormalEndException("取消自动任务");
        }

        if (millisecondsTimeout <= 0)
        {
            return;
        }

        var wait = CreateWait(runtime, ct);
        NewRetry.Do(() =>
        {
            if (ct.IsCancellationRequested)
            {
                throw new NormalEndException("取消自动任务");
            }

            TrySuspend(wait);
            CheckAndActivateGameWindow(runtime, wait);
        }, TimeSpan.FromSeconds(1), 100);
        wait.Sleep(millisecondsTimeout);
        if (ct.IsCancellationRequested)
        {
            throw new NormalEndException("取消自动任务");
        }
    }

    /// <summary>
    /// 异步等待。等待前的暂停与焦点恢复同样使用取消令牌，停止后不再阻塞任务收尾
    /// </summary>
    public static async Task Delay(this GameRuntime runtime, int millisecondsTimeout, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            throw new NormalEndException("取消自动任务");
        }

        if (millisecondsTimeout <= 0)
        {
            return;
        }

        var wait = CreateWait(runtime, ct);
        NewRetry.Do(() =>
        {
            if (ct.IsCancellationRequested)
            {
                throw new NormalEndException("取消自动任务");
            }

            TrySuspend(wait);
            CheckAndActivateGameWindow(runtime, wait);
        }, TimeSpan.FromSeconds(1), 100);

        // 运行环境停止时也要立即结束等待
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, runtime.Stopping);
        try
        {
            await Task.Delay(millisecondsTimeout, linked.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && runtime.IsStopping)
        {
            throw new NormalEndException("游戏运行环境已停止");
        }

        wait.ThrowIfStopped();
        if (ct.IsCancellationRequested)
        {
            throw new NormalEndException("取消自动任务");
        }
    }

    /// <summary>
    /// 等待快捷键解除暂停，运行环境停止后不继续等待
    /// </summary>
    public static void TrySuspend(this GameRuntime runtime) => TrySuspend(CreateWait(runtime, CancellationToken.None));

    /// <summary>
    /// 等待边界：本运行环境已停止或游戏已退出时中断
    /// </summary>
    private static GameTaskWait CreateWait(GameRuntime runtime, CancellationToken ct) =>
        new(() => !runtime.IsStopping && runtime.Window.IsAlive, ct);

    /// <summary>
    /// 在每轮暂停等待和恢复子任务前检查停止信号，取消时只归还本次暂停计数
    /// </summary>
    private static void TrySuspend(GameTaskWait wait)
    {
        wait.ThrowIfStopped();
        var first = true;
        var autoPickStopped = false;
        // 记录最开始的暂停状态
        var isSuspend = RunnerContext.Instance.IsSuspend;
        try
        {
            while (RunnerContext.Instance.IsSuspend)
            {
                wait.ThrowIfStopped();
                if (first)
                {
                    RunnerContext.Instance.StopAutoPick();
                    autoPickStopped = true;
                    // 使快捷键本身释放
                    wait.Sleep(300);
                    foreach (User32.VK key in Enum.GetValues(typeof(User32.VK)))
                    {
                        wait.ThrowIfStopped();
                        // 检查键是否被按下
                        if (IsKeyPressed(key))
                        {
                            Logger.LogWarning($"解除{key}的按下状态.");
                            InputHub.Foreground.Keyboard.KeyUp(key);
                        }
                    }

                    Logger.LogWarning("快捷键触发暂停，等待解除");
                    foreach (var item in RunnerContext.Instance.SuspendableDictionary)
                    {
                        wait.ThrowIfStopped();
                        item.Value.Suspend();
                    }

                    first = false;
                }

                wait.Sleep(1000);
            }

            wait.ThrowIfStopped();
            // 从暂停中解除
            if (isSuspend)
            {
                Logger.LogWarning("暂停已经解除");
                foreach (var item in RunnerContext.Instance.SuspendableDictionary)
                {
                    wait.ThrowIfStopped();
                    item.Value.Resume();
                }
            }
        }
        finally
        {
            // 即使取消发生在暂停内部，也平衡本次拾取暂停计数；不会恢复已停止的子任务。
            if (autoPickStopped)
            {
                RunnerContext.Instance.ResumeAutoPick();
            }
        }
    }

    /// <summary>
    /// 等待并恢复游戏焦点；取消、解绑或游戏退出时终止循环，不再尝试激活旧窗口
    /// </summary>
    private static void CheckAndActivateGameWindow(GameRuntime runtime, GameTaskWait wait)
    {
        wait.ThrowIfStopped();
        var window = runtime.Window;
        if (!window.RequiresForeground)
        {
            // 输入不依赖前台的运行环境（网页版）不检查焦点、不抢前台，
            // 只保证窗口没有最小化：最小化后截图器拿不到新帧
            if (window.IsMinimized)
            {
                Logger.LogInformation("游戏窗口已最小化，尝试还原");
                window.Activate();
            }

            return;
        }

        if (!Config.OtherConfig.RestoreFocusOnLostEnabled)
        {
            if (!runtime.IsOperable())
            {
                var name = SystemControl.GetActiveByProcess();
                Logger.LogWarning($"当前获取焦点的窗口为: {name}，不是原神，暂停");
                throw new RetryException("当前获取焦点的窗口不是原神");
            }
        }

        var count = 0;
        // 未激活则尝试恢复窗口
        while (!runtime.IsOperable())
        {
            wait.ThrowIfStopped();
            if (count >= 10 && count % 10 == 0)
            {
                Logger.LogInformation("多次尝试未恢复，尝试最小化后激活窗口！");
                SystemControl.MinimizeAndActivateWindow(window.Handle);
            }
            else
            {
                var name = SystemControl.GetActiveByProcess();
                Logger.LogInformation("当前获取焦点的窗口为: {Name}，不是原神，尝试恢复窗口", name);
                SystemControl.FocusWindow(window.Handle);
            }

            count++;
            wait.Sleep(1000);
        }

        wait.ThrowIfStopped();
    }

    /// <summary>
    /// 运行环境已经开始解绑时按正常中断处理
    /// </summary>
    /// <exception cref="NormalEndException">运行环境已停止</exception>
    public static void ThrowIfStopping(this GameRuntime runtime)
    {
        if (runtime.IsStopping)
        {
            throw new NormalEndException("游戏运行环境已停止");
        }
    }

    /// <summary>
    /// 查询按键是否处于按下状态
    /// </summary>
    private static bool IsKeyPressed(User32.VK key)
    {
        // 检查高位是否为 1（表示按键被按下）
        return (User32.GetAsyncKeyState((int)key) & 0x8000) != 0;
    }

    #endregion

    #region 窗口状态

    /// <summary>
    /// 现在能不能操作游戏：输入不依赖前台的运行环境（网页版）恒为 true，否则判断前台窗口是否属于游戏进程
    /// </summary>
    public static bool IsOperable(this GameRuntime runtime)
    {
        var window = runtime.Window;
        if (!window.RequiresForeground)
        {
            return true;
        }

        var name = SystemControl.GetActiveProcessName();
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return SystemControl.GetGenshinGameProcessNameList(window)
            .Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region 模拟操作

    /// <summary>
    /// 模拟长按指定动作。使用 try/finally 确保任务被取消或发生异常时按键也能释放，防止卡键
    /// </summary>
    /// <param name="runtime">运行环境</param>
    /// <param name="action">需要模拟的游戏动作（如元素战技、普通攻击等）</param>
    /// <param name="holdMs">长按持续的时间（毫秒）</param>
    /// <param name="ct">取消令牌</param>
    public static async Task SimulateHoldActionAsync(this GameRuntime runtime, GIActions action, int holdMs, CancellationToken ct)
    {
        try
        {
            InputHub.Foreground.SimulateAction(action, KeyType.KeyDown);
            await runtime.Delay(holdMs, ct);
        }
        finally
        {
            InputHub.Foreground.SimulateAction(action, KeyType.KeyUp);
        }
    }

    /// <summary>
    /// 模拟长按元素战技（如万叶长E）。包含释放前摇、长按以及释放后的缓冲延时
    /// </summary>
    /// <param name="runtime">运行环境</param>
    /// <param name="holdMs">元素战技按住的时间（毫秒）</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="releaseLeftMouseBefore">是否在按下元素战技前先松开鼠标左键，避免输入冲突</param>
    /// <param name="releaseLeftMouseDelayMs">松开鼠标左键后的缓冲时间（毫秒）</param>
    /// <param name="postKeyUpDelayMs">元素战技释放后的缓冲时间（毫秒）</param>
    public static async Task SimulateHoldElementalSkillAsync(
        this GameRuntime runtime,
        int holdMs,
        CancellationToken ct,
        bool releaseLeftMouseBefore = true,
        int releaseLeftMouseDelayMs = 10,
        int postKeyUpDelayMs = 50)
    {
        if (releaseLeftMouseBefore)
        {
            InputHub.Foreground.Mouse.LeftButtonUp();
            await runtime.Delay(releaseLeftMouseDelayMs, ct);
        }

        await runtime.SimulateHoldActionAsync(GIActions.ElementalSkill, holdMs, ct);
        await runtime.Delay(postKeyUpDelayMs, ct);
    }

    /// <summary>
    /// 模拟鼠标左键连续点击循环（如万叶长E后的下落攻击）。双层 try/finally 确保任何阶段取消或异常时左键都会释放
    /// </summary>
    /// <param name="runtime">运行环境</param>
    /// <param name="repeatCount">循环点击的次数</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="preUpDelayMs">每次点击前，预先抬起左键后的缓冲延时（毫秒）</param>
    /// <param name="downHoldMs">鼠标左键按下的保持时间（毫秒）</param>
    /// <param name="postUpDelayMs">每次点击完成后的等待时间（毫秒）</param>
    public static async Task SimulateMouseLeftClickLoopAsync(
        this GameRuntime runtime,
        int repeatCount,
        CancellationToken ct,
        int preUpDelayMs = 10,
        int downHoldMs = 35,
        int postUpDelayMs = 50)
    {
        try
        {
            for (var i = 0; i < repeatCount; i++)
            {
                InputHub.Foreground.Mouse.LeftButtonUp();
                await runtime.Delay(preUpDelayMs, ct);
                InputHub.Foreground.Mouse.LeftButtonDown();
                try
                {
                    await runtime.Delay(downHoldMs, ct);
                }
                finally
                {
                    InputHub.Foreground.Mouse.LeftButtonUp();
                }

                await runtime.Delay(postUpDelayMs, ct);
            }
        }
        finally
        {
            InputHub.Foreground.Mouse.LeftButtonUp();
        }
    }

    #endregion

    #region 按游戏捕获区域坐标点击

    /// <summary>
    /// 在游戏捕获区域维度进行点击
    /// </summary>
    /// <param name="runtime">运行环境</param>
    /// <param name="posFunc">
    /// 输出要点击的坐标（相对游戏捕获区域），参数为当前游戏捕获区域大小、捕获区域到 1080P 的缩放比例。
    /// 方法内的魔法数字必须是 1080P 下的数字
    /// </param>
    public static void GameRegionClick(this GameRuntime runtime, Func<Size, double, (double, double)> posFunc)
    {
        var (x, y) = ToDesktop(runtime, posFunc);
        DesktopRegion.DesktopRegionClick(x, y);
    }

    /// <summary>
    /// 在游戏捕获区域维度移动鼠标，参数含义同 <see cref="GameRegionClick"/>
    /// </summary>
    public static void GameRegionMove(this GameRuntime runtime, Func<Size, double, (double, double)> posFunc)
    {
        var (x, y) = ToDesktop(runtime, posFunc);
        DesktopRegion.DesktopRegionMove(x, y);
    }

    /// <summary>
    /// 按游戏捕获区域维度的位移相对移动鼠标，参数含义同 <see cref="GameRegionClick"/>
    /// </summary>
    public static void GameRegionMoveBy(this GameRuntime runtime, Func<Size, double, (double, double)> deltaFunc)
    {
        var systemInfo = runtime.SystemInfo;
        var captureAreaRect = systemInfo.CaptureAreaRect;
        var (dx, dy) = deltaFunc(new Size(captureAreaRect.Width, captureAreaRect.Height), systemInfo.ScaleTo1080PRatio);
        DesktopRegion.DesktopRegionMoveBy(dx, dy);
    }

    /// <summary>
    /// 输入 1080P 下的坐标，自动转换到当前游戏捕获区域大小下的坐标并点击
    /// </summary>
    public static void GameRegion1080PPosClick(this GameRuntime runtime, double cx, double cy)
    {
        runtime.GameRegionClick((_, scale) => (cx * scale, cy * scale));
    }

    /// <summary>
    /// 输入 1080P 下的坐标，自动转换到当前游戏捕获区域大小下的坐标并移动鼠标
    /// </summary>
    public static void GameRegion1080PPosMove(this GameRuntime runtime, double cx, double cy)
    {
        runtime.GameRegionMove((_, scale) => (cx * scale, cy * scale));
    }

    /// <summary>
    /// 把游戏捕获区域内的坐标换算为桌面坐标
    /// </summary>
    private static (double X, double Y) ToDesktop(GameRuntime runtime, Func<Size, double, (double, double)> posFunc)
    {
        var systemInfo = runtime.SystemInfo;
        var captureAreaRect = systemInfo.CaptureAreaRect;
        var (cx, cy) = posFunc(new Size(captureAreaRect.Width, captureAreaRect.Height), systemInfo.ScaleTo1080PRatio);
        return (captureAreaRect.X + cx, captureAreaRect.Y + cy);
    }

    #endregion
}
