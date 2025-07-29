using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Model.Gear.Tasks;

namespace BetterGenshinImpact.Model.Gear.Triggers;

/// <summary>
/// 定时触发器
/// </summary>
public class TimedGearTrigger : GearBaseTrigger
{
    /// <summary>
    /// 触发间隔（毫秒）
    /// </summary>
    public int IntervalMs { get; set; } = 5000;

    /// <summary>
    /// 是否重复执行
    /// </summary>
    public bool IsRepeating { get; set; } = true;

    /// <summary>
    /// 延迟启动时间（毫秒）
    /// </summary>
    public int DelayMs { get; set; } = 0;

    /// <summary>
    /// 最大执行次数（0表示无限制）
    /// </summary>
    public int MaxExecutions { get; set; } = 0;

    private int _executionCount = 0;
    private CancellationTokenSource? _cancellationTokenSource;

    public override async Task Run()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _executionCount = 0;

        // 延迟启动
        if (DelayMs > 0)
        {
            await Task.Delay(DelayMs, _cancellationTokenSource.Token);
        }

        do
        {
            if (_cancellationTokenSource.Token.IsCancellationRequested)
                break;

            // 执行任务
            await ExecuteTasks();
            _executionCount++;

            // 检查是否达到最大执行次数
            if (MaxExecutions > 0 && _executionCount >= MaxExecutions)
                break;

            // 如果不重复执行，则退出
            if (!IsRepeating)
                break;

            // 等待下次执行
            await Task.Delay(IntervalMs, _cancellationTokenSource.Token);
        }
        while (IsRepeating && !_cancellationTokenSource.Token.IsCancellationRequested);
    }

    /// <summary>
    /// 停止触发器
    /// </summary>
    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
    }

    private async Task ExecuteTasks()
    {
        List<BaseGearTask> list = GearTaskRefenceList.Select(gearTask => gearTask.ToGearTask()).ToList();
        foreach (var gearTask in list)
        {
            if (_cancellationTokenSource?.Token.IsCancellationRequested == true)
                break;
            await gearTask.Run(_cancellationTokenSource?.Token ?? CancellationToken.None);
        }
    }
}