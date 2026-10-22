using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
namespace BetterGenshinImpact.GameTask.AutoOpenChest;

/// <summary>
/// 识别宝箱图标，走向宝箱并开启。
/// </summary>
public class AutoOpenChestTask : ISoloTask<NoParam, NoData>
{
    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 创建开宝箱任务
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    public AutoOpenChestTask(GameRuntime runtime)
    {
        _runtime = runtime;
    }

    /// <summary>
    /// 识别宝箱图标并走过去开启。画面中没有宝箱交互提示时跳过；交互后没有确认是否开启，返回未确认。取消时抛出异常
    /// </summary>
    /// <param name="param">空参数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>任务结果</returns>
    public async Task<SoloTaskResult<NoData>> RunAsync(NoParam param, CancellationToken ct)
    {
        using var initialCapture = _runtime.CaptureToRectArea();

        // 一开始没有宝箱交互提示，不执行
        if (!initialCapture.Find(RecognitionAssets.Get("AutoOpenChest", "ChestFIcon", initialCapture)).IsExist())
        {
            return SoloTaskResult.Skipped("chest_not_found", "未识别到宝箱");
        }

        using CancellationTokenSource _ct = new();
        using var cancellationRegistration = ct.Register(_ct.Cancel);
        bool isFlower = false; // 是否是地脉花
        bool interacted = false; // 是否已经按下交互键
        // 限制寻找宝箱的时间
        var timeLimit = 60;
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeLimit), _ct.Token)
            .ContinueWith(_ => _ct.Cancel(), TaskContinuationOptions.OnlyOnRanToCompletion);
        try
        {
            while (!_ct.IsCancellationRequested)
            {
                using var ra = _runtime.CaptureToRectArea();
                Region chestIcon = ra.Find(RecognitionAssets.Get("AutoOpenChest", "ChestIcon", ra));
                int limit = chestIcon.Width;
                if (!chestIcon.IsExist())
                {
                    Logger.LogInformation("未找到宝箱图标");
                    return SoloTaskResult.Failed("chest_not_found", "未找到宝箱图标");
                }

                if (ra.Find(RecognitionAssets.Get("AutoOpenChest", "ChestFIcon", ra)).IsExist()
                    || ra.Find(RecognitionAssets.Get("AutoOpenChest", "FlowerFIcon", ra)).IsExist())
                {
                    // 找到宝箱/ 地脉花
                    isFlower = ra.Find(RecognitionAssets.Get("AutoOpenChest", "FlowerFIcon", ra)).IsExist();
                    InputHub.Foreground.SimulateAction(GIActions.PickUpOrInteract, KeyType.KeyPress);
                    InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                    interacted = true;
                    break;
                }

                if (Math.Abs(chestIcon.Width / 2 - chestIcon.X) < limit)
                {
                    InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                }

                if (chestIcon.Y > 600)
                {
                    // 若宝箱图标在下方就表示宝箱在后面。
                    InputHub.Foreground.SimulateAction(GIActions.MoveBackward);
                    await _runtime.Delay(30, _ct.Token);
                    InputHub.Foreground.Mouse.MiddleButtonClick();
                }
                else
                {
                    var gap = ra.Width / 2 - chestIcon.X;
                    int rate = 2;
                    InputHub.Foreground.Mouse.MoveMouseBy(gap / rate, 0);
                }

                await _runtime.Delay(500, _ct.Token);
            }
        }
        catch (NormalEndException) when (!ct.IsCancellationRequested && !_runtime.IsStopping)
        {
            // 内部计时到期取消了等待，按超时处理；外部取消和运行环境停止仍向上抛出
        }
        finally
        {
            // 如果循环提前退出，取消计时任务
            InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            _ct.Cancel();
            try
            {
                await timeoutTask; // 等待超时任务结束
            }
            catch (OperationCanceledException)
            {
                // 计时被提前取消时延续任务也会被取消，忽略
            }
        }

        // 外部取消时向上抛出，不当作超时
        ct.ThrowIfCancellationRequested();
        if (!interacted)
        {
            return SoloTaskResult.Failed(SoloTaskReasons.LimitReached, $"{timeLimit} 秒内未走到宝箱");
        }

        // TODO : 是否考虑兼容地脉花 以及地脉花的获取策略

        if (isFlower) {
            // 地脉花策略
            flowerHandle();
        }

        // 只按下了交互键，没有确认宝箱是否已开启
        return SoloTaskResult.Unverified(isFlower ? "已与地脉花交互" : "已与宝箱交互");
    }

    private async void flowerHandle()
    {
        InputHub.Foreground.SimulateAction(GIActions.OpenPaimonMenu);
    }


}
