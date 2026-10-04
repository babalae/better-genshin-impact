using BetterGenshinImpact.Core.Input;
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Model.Area;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Vanara.PInvoke;
using BetterGenshinImpact.Core.Simulator.Extensions;

namespace BetterGenshinImpact.GameTask.Common;

public class TaskControl
{
    public static ILogger Logger { get; } = App.GetLogger<TaskControl>();

    public static readonly SemaphoreSlim TaskSemaphore = new(1, 1);


    /// <summary>
    /// 检查暂停与游戏焦点后等待；运行环境停止时中断旧的无令牌调用。
    /// </summary>
    public static void CheckAndSleep(int millisecondsTimeout)
    {
        var wait = CreateWait(CancellationToken.None);
        TrySuspend(wait);
        CheckAndActivateGameWindow(wait);

        wait.Sleep(millisecondsTimeout);
    }

    /// <summary>
    /// 重试焦点检查后等待，并在等待期间检查原游戏运行环境是否已停止。
    /// </summary>
    public static void Sleep(int millisecondsTimeout)
    {
        var wait = CreateWait(CancellationToken.None);
        NewRetry.Do(() =>
        {
            TrySuspend(wait);
            CheckAndActivateGameWindow(wait);
        }, TimeSpan.FromSeconds(1), 100);
        wait.Sleep(millisecondsTimeout);
    }

    /// <summary>
    /// 固定本次等待的运行环境，避免停止后使用遗留句柄，或误操作新绑定的另一环境。
    /// </summary>
    private static GameTaskWait CreateWait(CancellationToken ct)
    {
        var runtime = TaskContext.Instance().Runtime;
        return new GameTaskWait(() => runtime is not null
            && ReferenceEquals(TaskContext.Instance().Runtime, runtime)
            && runtime.Window.IsAlive, ct);
    }

    private static bool IsKeyPressed(User32.VK key)
    {
        // 获取按键状态
        var state = User32.GetAsyncKeyState((int)key);

        // 检查高位是否为 1（表示按键被按下）
        return (state & 0x8000) != 0;
    }

    /// <summary>
    /// 等待快捷键解除暂停，运行环境停止后不继续等待。
    /// </summary>
    public static void TrySuspend() => TrySuspend(CreateWait(CancellationToken.None));

    /// <summary>
    /// 在每轮暂停等待和恢复子任务前检查停止信号，取消时只归还本次暂停计数。
    /// </summary>
    private static void TrySuspend(GameTaskWait wait)
    {
        wait.ThrowIfStopped();
        var first = true;
        var autoPickStopped = false;
        //此处为了记录最开始的暂停状态
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
                    //使快捷键本身释放
                    wait.Sleep(300);
                    foreach (User32.VK key in Enum.GetValues(typeof(User32.VK)))
                    {
                        wait.ThrowIfStopped();
                        // 检查键是否被按下
                        if (IsKeyPressed(key)) // 强制转换 VK 枚举为 int
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
            //从暂停中解除
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
                RunnerContext.Instance.ResumeAutoPick();
        }
    }

    /// <summary>
    /// 等待并恢复游戏焦点；取消、解绑或游戏退出时终止循环，不再尝试激活旧窗口。
    /// </summary>
    private static void CheckAndActivateGameWindow(GameTaskWait wait)
    {
        wait.ThrowIfStopped();
        var window = TaskContext.Instance().Runtime?.Window;
        if (window is { RequiresForeground: false })
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

        if (!TaskContext.Instance().Config.OtherConfig.RestoreFocusOnLostEnabled)
        {
            if (!SystemControl.IsGenshinImpactActiveByProcess())
            {
                var name = SystemControl.GetActiveByProcess();
                Logger.LogWarning($"当前获取焦点的窗口为: {name}，不是原神，暂停");
                throw new RetryException("当前获取焦点的窗口不是原神");
            }
        }

        var count = 0;
        //未激活则尝试恢复窗口
        while (!SystemControl.IsGenshinImpactActiveByProcess())
        {
            wait.ThrowIfStopped();
            if (count >= 10 && count % 10 == 0)
            {
                Logger.LogInformation("多次尝试未恢复，尝试最小化后激活窗口！");
                SystemControl.MinimizeAndActivateWindow(TaskContext.Instance().GameHandle);
            }
            else
            {
                var name = SystemControl.GetActiveByProcess();
                Logger.LogInformation("当前获取焦点的窗口为: {Name}，不是原神，尝试恢复窗口", name);
                SystemControl.FocusWindow(TaskContext.Instance().GameHandle);
            }

            count++;
            wait.Sleep(1000);
        }
        wait.ThrowIfStopped();
    }

    /// <summary>
    /// 同步等待并把本轮取消令牌传入暂停、焦点恢复和延时内部。
    /// </summary>
    public static void Sleep(int millisecondsTimeout, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            throw new NormalEndException("取消自动任务");
        }

        if (millisecondsTimeout <= 0)
        {
            return;
        }

        var wait = CreateWait(ct);
        NewRetry.Do(() =>
        {
            if (ct.IsCancellationRequested)
            {
                throw new NormalEndException("取消自动任务");
            }

            TrySuspend(wait);
            CheckAndActivateGameWindow(wait);
        }, TimeSpan.FromSeconds(1), 100);
        wait.Sleep(millisecondsTimeout);
        if (ct.IsCancellationRequested)
        {
            throw new NormalEndException("取消自动任务");
        }
    }

    /// <summary>
    /// 异步等待前的同步暂停与焦点恢复同样使用本轮取消令牌，停止后不再阻塞任务收尾。
    /// </summary>
    public static async Task Delay(int millisecondsTimeout, CancellationToken ct)
    {
        if (ct is { IsCancellationRequested: true })
        {
            throw new NormalEndException("取消自动任务");
        }

        if (millisecondsTimeout <= 0)
        {
            return;
        }

        var wait = CreateWait(ct);
        NewRetry.Do(() =>
        {
            if (ct is { IsCancellationRequested: true })
            {
                throw new NormalEndException("取消自动任务");
            }

            TrySuspend(wait);
            CheckAndActivateGameWindow(wait);
        }, TimeSpan.FromSeconds(1), 100);
        await Task.Delay(millisecondsTimeout, ct);
        wait.ThrowIfStopped();
        if (ct is { IsCancellationRequested: true })
        {
            throw new NormalEndException("取消自动任务");
        }
    }

    /// <summary>
    /// 模拟长按指定动作。使用 try/finally 块确保在任务被取消或发生异常时，按键也能安全释放，防止卡键。
    /// </summary>
    /// <param name="action">需要模拟的游戏动作（如元素战技、普通攻击等）</param>
    /// <param name="holdMs">长按持续的时间（毫秒）</param>
    /// <param name="ct">用于监控任务取消的取消令牌</param>
    public static async Task SimulateHoldActionAsync(GIActions action, int holdMs, CancellationToken ct)
    {
        try
        {
            InputHub.Foreground.SimulateAction(action, KeyType.KeyDown);
            await Delay(holdMs, ct);
        }
        finally
        {
            InputHub.Foreground.SimulateAction(action, KeyType.KeyUp);        
        }
    }

    /// <summary>
    /// 模拟长按元素战技（如万叶长E）。包含释放前摇、长按以及释放后的缓冲延时。
    /// </summary>
    /// <param name="holdMs">元素战技按住的时间（毫秒）</param>
    /// <param name="ct">用于监控任务取消的取消令牌</param>
    /// <param name="releaseLeftMouseBefore">是否在按下元素战技前先松开鼠标左键，避免输入冲突，默认 true</param>
    /// <param name="releaseLeftMouseDelayMs">松开鼠标左键后的缓冲时间（毫秒），默认 10ms</param>
    /// <param name="postKeyUpDelayMs">元素战技释放后的缓冲时间（毫秒），默认 50ms</param>
    public static async Task SimulateHoldElementalSkillAsync(
        int holdMs,
        CancellationToken ct,
        bool releaseLeftMouseBefore = true,
        int releaseLeftMouseDelayMs = 10,
        int postKeyUpDelayMs = 50)
    {
        if (releaseLeftMouseBefore)
        {
            InputHub.Foreground.Mouse.LeftButtonUp();
            await Delay(releaseLeftMouseDelayMs, ct);
        }

        await SimulateHoldActionAsync(GIActions.ElementalSkill, holdMs, ct);   
        await Delay(postKeyUpDelayMs, ct);
    }

    /// <summary>
    /// 模拟鼠标左键连续点击循环（如万叶长E后的下落攻击）。双层 try/finally 设计以确保无论在循环的哪个阶段发生取消或异常，鼠标左键都会被强制释放。
    /// </summary>
    /// <param name="repeatCount">需要循环点击的次数</param>
    /// <param name="ct">用于监控任务取消的取消令牌</param>
    /// <param name="preUpDelayMs">每次点击前，预先抬起左键后的缓冲延时（毫秒），默认 10ms</param>
    /// <param name="downHoldMs">鼠标左键按下的保持时间（毫秒），默认 35ms</param>
    /// <param name="postUpDelayMs">每次点击完成后的等待时间（毫秒），默认 50ms</param>
    public static async Task SimulateMouseLeftClickLoopAsync(
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
                await Delay(preUpDelayMs, ct);
                InputHub.Foreground.Mouse.LeftButtonDown();
                try
                {
                    await Delay(downHoldMs, ct);
                }
                finally
                {
                    InputHub.Foreground.Mouse.LeftButtonUp();
                }

                await Delay(postUpDelayMs, ct);
            }
        }
        finally
        {
            InputHub.Foreground.Mouse.LeftButtonUp();
        }
    }

    public static Mat CaptureGameImage(IGameCapture? gameCapture)
    {
        var captureFrame = gameCapture?.Capture();
        var image = captureFrame?.Frame;
        if (image == null)
        {
            captureFrame?.Dispose();
            Logger.LogWarning("截图失败!");
            // 重试3次
            for (var i = 0; i < 3; i++)
            {
                captureFrame = gameCapture?.Capture();
                image = captureFrame?.Frame;
                if (image != null)
                {
                    return image;
                }

                captureFrame?.Dispose();
                Sleep(30);
            }

            throw new Exception("尝试多次后,截图失败!");
        }
        else
        {
            return image;
        }
    }

    public static Mat? CaptureGameImageNoRetry(IGameCapture? gameCapture)
    {
        return gameCapture?.Capture()?.Frame;
    }

    /// <summary>
    /// 自动判断当前运行上下文中截图方式，并选择合适的截图方式返回
    /// </summary>
    /// <returns></returns>
    public static ImageRegion CaptureToRectArea(bool forceNew = false)
    {
        var capture = TaskContext.Instance().Runtime?.Capture
                      ?? throw new InvalidOperationException("截图器未初始化!");
        var image = CaptureGameImage(capture);
        var content = new CaptureContent(image, 0, 0);
        return content.CaptureRectArea;
    }
}
