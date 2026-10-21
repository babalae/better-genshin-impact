using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 截图器刚启动时，等待游戏进入可执行任务的界面（主界面、可关闭界面或秘境）。
/// 进入游戏本身由「自动开门」触发器负责；这里只负责等待，并在长时间未就绪时恢复焦点、把鼠标移回游戏内
/// </summary>
/// <param name="runtime">本次运行使用的运行环境</param>
public class WaitForGameReadyTask(GameRuntime runtime)
{
    /// <summary>
    /// 开始识别前的等待，给截图器出第一帧的时间
    /// </summary>
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// 两次识别之间的间隔
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 等待超过这个时间仍未就绪时，开始恢复焦点、移动鼠标
    /// </summary>
    private static readonly TimeSpan AssistAfter = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 移动鼠标的截止时间，之后不再干扰用户的鼠标
    /// </summary>
    private static readonly TimeSpan MouseNudgeUntil = TimeSpan.FromSeconds(200);

    /// <summary>
    /// 总等待上限。自动开门触发器 5 分钟后自停，超过上限仍未进入游戏时让本轮任务失败，避免无人值守时一直卡住
    /// </summary>
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 每连续失焦这么多次，改用"最小化后激活"恢复一次，与 GameRuntimeExtensions 的焦点恢复策略一致
    /// </summary>
    private const int MinimizeActivateEvery = 10;

    /// <summary>
    /// 鼠标归位的 1080P 坐标，位于游戏画面内且远离常用按钮
    /// </summary>
    private const double MouseNudgeX = 300, MouseNudgeY = 300;

    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime = runtime;

    /// <summary>
    /// 连续检测到失焦的次数，恢复焦点后清零
    /// </summary>
    private int _focusLostCount;

    /// <summary>
    /// 任务名称
    /// </summary>
    public string Name => "等待进入游戏";

    /// <summary>
    /// 循环识别直到游戏进入可执行任务的界面。
    /// 等待期间截图器被停止时，截图会按正常中断（NormalEndException）结束本轮任务
    /// </summary>
    /// <param name="ct">当前任务的取消令牌</param>
    /// <exception cref="TimeoutException">超过 <see cref="WaitTimeout"/> 仍未进入游戏</exception>
    public async Task Start(CancellationToken ct)
    {
        await Task.Delay(InitialDelay, ct);
        var elapsed = Stopwatch.StartNew();
        var hinted = false;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (IsReady())
            {
                return;
            }

            // 首次未就绪时提示用户，只提示一次
            if (!hinted)
            {
                hinted = true;
                LogWaitingHint();
            }

            if (elapsed.Elapsed >= WaitTimeout)
            {
                throw new TimeoutException($"等待进入游戏超时（{WaitTimeout.TotalMinutes:0} 分钟），本次任务不再执行");
            }

            await Task.Delay(PollInterval, ct);

            // 长时间未就绪：尝试排除失焦、鼠标在游戏外这两类会让自动开门卡住的情况
            if (elapsed.Elapsed >= AssistAfter)
            {
                AssistEnterGame(elapsed.Elapsed);
            }
        }
    }

    /// <summary>
    /// 截取一帧并判断是否已进入可执行任务的界面。截图前会检查运行环境是否已停止
    /// </summary>
    private bool IsReady()
    {
        using var region = _runtime.CaptureToRectArea();
        return Bv.IsInGameReadyUi(region);
    }

    /// <summary>
    /// 输出等待提示
    /// </summary>
    private static void LogWaitingHint()
    {
        Logger.LogInformation("当前不在游戏主界面，等待进入主界面后执行任务...");
        Logger.LogInformation("如果你已经在游戏内的其他界面，请自行退出当前界面（ESC），或是30秒后将程序将自动尝试到入主界面，使当前任务能够继续运行！");
    }

    /// <summary>
    /// 长时间未就绪时的辅助操作：恢复焦点，并在截止时间前把鼠标移回游戏内
    /// </summary>
    /// <param name="elapsed">已等待的时间</param>
    private void AssistEnterGame(TimeSpan elapsed)
    {
        // 防止自启动游戏后因为一些原因失焦，导致一直卡住
        RecoverFocusIfLost();

        // 自动开门点击的是鼠标当前位置，自启动游戏时鼠标若在游戏外面将无法自动开门，这里尝试移动到游戏界面
        if (elapsed < MouseNudgeUntil)
        {
            _runtime.GameRegion1080PPosMove(MouseNudgeX, MouseNudgeY);
        }
    }

    /// <summary>
    /// 游戏不在前台时尝试恢复：平时直接激活窗口，每连续失焦 <see cref="MinimizeActivateEvery"/> 次改用最小化后激活
    /// </summary>
    private void RecoverFocusIfLost()
    {
        if (_runtime.IsOperable())
        {
            _focusLostCount = 0;
            return;
        }

        _focusLostCount++;
        if (_focusLostCount % MinimizeActivateEvery == 0)
        {
            Logger.LogInformation("多次尝试未恢复，尝试最小化后激活窗口！");
            SystemControl.MinimizeAndActivateWindow(_runtime.Window.Handle);
        }
        else
        {
            _runtime.Window.Activate();
        }
    }
}
