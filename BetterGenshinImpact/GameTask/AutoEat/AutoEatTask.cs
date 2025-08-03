using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoEat;

/// <summary>
/// 自动吃药任务
/// 检测红血自动使用便携营养袋
/// </summary>
public class AutoEatTask : BaseIndependentTask, ISoloTask
{
    public string Name => "自动吃药";

    private readonly AutoEatParam _taskParam;
    private readonly AutoEatConfig _config;
    private CancellationToken _ct;

    public AutoEatTask(AutoEatParam taskParam)
    {
        _taskParam = taskParam;
        _config = TaskContext.Instance().Config.AutoEatConfig;
    }

    public async Task Start(CancellationToken ct)
    {
        _ct = ct;

        Init();
        Logger.LogInformation("自动吃药任务启动");

        if (!IsTakeFood())
        {
            Logger.LogWarning("未装备 \"{Tool}\"，无法启用自动吃药功能", "便携营养袋");
            return;
        }

        try
        {
            await AutoEatLoop();
        }
        catch (Exception e)
        {
            Logger.LogError(e, "自动吃药任务发生异常");
            throw;
        }
        finally
        {
            Logger.LogInformation("自动吃药任务结束");
        }
    }

    private void Init()
    {
        Logger.LogInformation("→ {Text} 检测间隔: {Interval}ms", "自动吃药，", _config.CheckInterval);
        Logger.LogInformation("→ {Text} 吃药间隔: {Interval}ms", "自动吃药，", _config.EatInterval);
    }

    /// <summary>
    /// 自动吃药主循环
    /// </summary>
    private async Task AutoEatLoop()
    {
        var lastEatTime = DateTime.MinValue;

        while (!_ct.IsCancellationRequested)
        {
            try
            {
                // 检测当前角色是否红血
                if (Bv.CurrentAvatarIsLowHp(CaptureToRectArea()))
                {
                    var now = DateTime.Now;
                    // 检查是否超过吃药间隔时间，避免重复吃药
                    if ((now - lastEatTime).TotalMilliseconds >= _config.EatInterval)
                    {
                        // 模拟按键 "Z" 使用便携营养袋
                        Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);
                        lastEatTime = now;
                        
                        Logger.LogInformation("检测到红血，自动吃药");
                    }
                }

                await Delay(_config.CheckInterval, _ct);
            }
            catch (OperationCanceledException)
            {
                // 任务被取消，正常退出
                break;
            }
            catch (Exception e)
            {
                Logger.LogDebug(e, "自动吃药检测时发生异常");
                await Delay(1000, _ct); // 异常时稍作等待
            }
        }
    }

    /// <summary>
    /// 检测是否装备了便携营养袋
    /// </summary>
    private bool IsTakeFood()
    {
        try
        {
            // 获取图像
            using var ra = CaptureToRectArea();
            // 识别道具图标下是否是数字
            var s = TaskContext.Instance().SystemInfo.AssetScale;
            var countArea = ra.DeriveCrop(1800 * s, 845 * s, 40 * s, 20 * s);
            var count = OcrFactory.Paddle.OcrWithoutDetector(countArea.CacheGreyMat);
            return int.TryParse(count, out _);
        }
        catch (Exception e)
        {
            Logger.LogDebug(e, "检测便携营养袋时发生异常");
            return false;
        }
    }
}