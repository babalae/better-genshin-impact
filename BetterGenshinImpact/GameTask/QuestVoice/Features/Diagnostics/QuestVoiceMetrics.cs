using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;

/// <summary>本轮计数/总时长/最大时长；仅快照时分配，不记录对白或资产路径。</summary>
public sealed class QuestVoiceMetrics
{
    /// <summary>各阶段原子计数。</summary>
    private readonly long[] _counts = new long[Enum.GetValues<QuestVoiceMetric>().Length];
    /// <summary>各阶段累计Stopwatch ticks。</summary>
    private readonly long[] _ticks = new long[Enum.GetValues<QuestVoiceMetric>().Length];
    /// <summary>各阶段最大Stopwatch ticks。</summary>
    private readonly long[] _maximum = new long[Enum.GetValues<QuestVoiceMetric>().Length];
    /// <summary>记录一次阶段完成，elapsedTicks为0时只计数。</summary>
    public void Record(QuestVoiceMetric metric, long elapsedTicks = 0)
    {
        var index = (int)metric;
        Interlocked.Increment(ref _counts[index]); Interlocked.Add(ref _ticks[index], elapsedTicks);
        var previous = Volatile.Read(ref _maximum[index]);
        while (elapsedTicks > previous)
        {
            var observed = Interlocked.CompareExchange(ref _maximum[index], elapsedTicks, previous);
            if (observed == previous) break;
            previous = observed;
        }
    }
    /// <summary>低频近似时刻快照；停止排空后为最终值。</summary>
    public IReadOnlyDictionary<QuestVoiceMetric, QuestVoiceMeasurement> Snapshot()
    {
        var result = new Dictionary<QuestVoiceMetric, QuestVoiceMeasurement>();
        foreach (var metric in Enum.GetValues<QuestVoiceMetric>())
        {
            var index = (int)metric;
            result[metric] = new(Volatile.Read(ref _counts[index]),
                Volatile.Read(ref _ticks[index]) * 1000d / Stopwatch.Frequency,
                Volatile.Read(ref _maximum[index]) * 1000d / Stopwatch.Frequency);
        }
        return result;
    }
}
/// <summary>Count为边界次数，时长单位毫秒，不代表GPU kernel时长。</summary>
public sealed record QuestVoiceMeasurement(long Count, double TotalMilliseconds, double MaximumMilliseconds);
/// <summary>只测现有边界，不改变调度和推理参数。</summary>
public enum QuestVoiceMetric
{
    /// <summary>宿主成功捕获后发给配音的回调。</summary>
    ReceivedFrame,
    /// <summary>忙或未到采样间隔。</summary>
    FilteredFrame,
    /// <summary>Tick内复制。</summary>
    Clone,
    /// <summary>后台BMP编码。</summary>
    Encode,
    /// <summary>主界面/对白模板检查。</summary>
    ExitEvidence,
    /// <summary>场景和签名检查。</summary>
    Inspection,
    /// <summary>OCR候选单槽等待。</summary>
    OcrQueue,
    /// <summary>整批OCR。</summary>
    OcrBatch,
    /// <summary>裁剪和张量前处理。</summary>
    Preprocess,
    /// <summary>模型/字典/原生会话初始化尝试。</summary>
    Initialization,
    /// <summary>进入原生Run。</summary>
    NativeRunStarted,
    /// <summary>原生Run墙钟，包括CPU和EP调度。</summary>
    NativeRun,
    /// <summary>输出复制和CTC。</summary>
    Ctc,
    /// <summary>拥有原生资源后的取消。</summary>
    Cancelled,
    /// <summary>废弃不可信原生资源。</summary>
    Retired,
    /// <summary>引擎关闭请求，后续调用将被拒绝。</summary>
    Closing,
    /// <summary>引擎等待在途调用并释放资源的耗时。</summary>
    Disposal
}
