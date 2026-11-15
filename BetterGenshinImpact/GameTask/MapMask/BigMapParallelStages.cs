using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.MapMask;

/// <summary>并行运行三个定位分支，有效坐标立即通知调用方，资源仍在全部分支退出后统一回收。</summary>
internal static class BigMapParallelStages
{
    /// <summary>地下先发布平面坐标再确认楼层；地表选出结果后立即发布并取消其它分支。</summary>
    internal static async Task<BigMapStageResult> RunAsync(
        Func<CancellationToken, Task<BigMapStageResult>> anchors,
        Func<CancellationToken, Task<BigMapStageResult>> surface,
        Func<CancellationToken, Task<BigMapStageResult>> layers,
        bool underground, CancellationToken cancellationToken, Action<BigMapStageResult>? resultReady = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var operations = new[] { anchors, surface, layers };
        var tasks = operations.Select((operation, i) =>
        {
            var submitted = Stopwatch.GetTimestamp();
            return Task.Run(async () =>
            {
                var queued = Stopwatch.GetElapsedTime(submitted).TotalMilliseconds;
                var timer = Stopwatch.StartNew();
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var result = await operation(cancellation.Token).ConfigureAwait(false);
                    return result with { Diagnostics = $"L{i + 3}: {result.Diagnostics}（排队 {queued:F1}ms，执行 {timer.ElapsedMilliseconds}ms）" };
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    return new BigMapStageResult(null, $"L{i + 3}: 已取消（排队 {queued:F1}ms，执行 {timer.ElapsedMilliseconds}ms）");
                }
                catch (Exception e)
                {
                    App.GetLogger<BigMapCascadeMatcher>().LogDebug(e, "大地图 L{Stage} 分支失败", i + 3);
                    return new BigMapStageResult(null, $"L{i + 3}: 分支异常（{e.GetType().Name}）");
                }
            });
        }).ToArray();
        var pending = new HashSet<Task<BigMapStageResult>>(tasks);
        MapMatchResult? selected = null;
        MapMatchResult? published = null;

        // 只读取已完成任务的不可变结果；发布时未完成的分支仍拥有图像，不能提前释放。
        string Diagnostics() => string.Join("；", tasks.Select((task, i) => task.IsCompletedSuccessfully
            ? task.Result.Diagnostics : $"L{i + 3}: 处理中"));
        // 同一结果只通知一次，后续确认楼层时才升级，避免重复刷新视口。
        void Publish(MapMatchResult? result)
        {
            if (result != null && result != published)
            {
                cancellationToken.ThrowIfCancellationRequested();
                resultReady?.Invoke(new BigMapStageResult(result, Diagnostics()));
                published = result;
            }
        }
        try
        {
            while (pending.Count > 0)
            {
                var finished = await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(finished);
                cancellationToken.ThrowIfCancellationRequested();
                var completed = tasks.Select(t => t.IsCompletedSuccessfully ? t.Result.Result : null).ToArray();
                selected = underground ? completed[2] : completed[0] ?? completed[1];
                // 地下的锚点/地表背景已经能确定坐标，无需等楼层或慢分支回收才显示。
                Publish(underground ? completed[2] ?? completed[0] ?? completed[1] : selected);
                if (selected != null)
                {
                    break;
                }
            }
        }
        finally
        {
            // 坐标已先通知调用方；后台仍须等原生计算退出，才能释放 Mat 或开始下一帧匹配。
            cancellation.Cancel();
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        selected ??= tasks[0].Result.Result ?? tasks[1].Result.Result ?? tasks[2].Result.Result;
        Publish(selected);
        return new BigMapStageResult(selected, string.Join("；", tasks.Select(t => t.Result.Diagnostics)));
    }

}

/// <summary>单个定位分支的结果及独立诊断，避免并行线程修改同一段文本。</summary>
/// <param name="Result">已通过本分支核验的视口。</param>
/// <param name="Diagnostics">本分支的实际执行结果。</param>
internal sealed record BigMapStageResult(MapMatchResult? Result, string Diagnostics);
