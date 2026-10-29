// 移植自 QuestVoice 1fc59d9c；会话寿命由宿主单轮 QuestVoiceSession 拥有。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

/// <summary>串行复用本轮DirectML会话；原生取消/错误后废弃。</summary>
public sealed class DirectMlOcrEngine : IOcrEngine, IDisposable
{
    /// <summary>保护会话状态与Run；不使用AvailableWaitHandle。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>本轮低开销阶段计数。</summary>
    private readonly QuestVoiceMetrics _metrics;
    /// <summary>原生资源工厂，用于分离外部推理与所有权逻辑。</summary>
    private readonly Func<OcrSettings, string, QuestVoiceMetrics, IDirectMlOcrSession> _factory;
    /// <summary>仅在gate内使用的原生资源。</summary>
    private IDirectMlOcrSession? _session;
    /// <summary>首次调用冻结模型与设备。</summary>
    private OcrSettings? _settings;
    /// <summary>先标记关闭再排空在途调用。</summary>
    private int _disposed;
    /// <summary>原生模型延迟到首次OCR初始化。</summary>
    public DirectMlOcrEngine(QuestVoiceMetrics? metrics = null)
        : this((settings, directory, counters) => new DirectMlOcrSession(settings, directory, counters), metrics) { }
    /// <summary>注入外部原生边界，确定性测试不替代引擎所有权。</summary>
    internal DirectMlOcrEngine(Func<OcrSettings, string, QuestVoiceMetrics, IDirectMlOcrSession> factory,
        QuestVoiceMetrics? metrics = null)
    { _factory = factory; _metrics = metrics ?? new(); }
    /// <summary>读取不含文本/路径的低频计数快照。</summary>
    public IReadOnlyDictionary<QuestVoiceMetric, QuestVoiceMeasurement> Diagnostics => _metrics.Snapshot();
    /// <summary>在取消检查和冻结配置约束下识别；同一会话绝不并发Run。</summary>
    public async Task<(IReadOnlyList<OcrText> Texts, OcrModelInfo Model)> RecognizeAsync(
        IReadOnlyList<OcrTensor> tensors, OcrSettings settings, string profileDirectory, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var normalized = settings with { ModelPath = Path.GetFullPath(settings.ModelPath) };
            if (_settings is { } frozen && (frozen.ModelPath != normalized.ModelPath || frozen.DeviceId != normalized.DeviceId))
                throw new OcrException(OcrErrorCode.Configuration, "本轮OCR模型与设备已冻结，请停止后重新启动配音。");
            _settings ??= normalized;
            try
            {
                return await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_session is null)
                    {
                        var start = Stopwatch.GetTimestamp();
                        try { _session = _factory(_settings, profileDirectory, _metrics); }
                        finally { _metrics.Record(QuestVoiceMetric.Initialization, Stopwatch.GetTimestamp() - start); }
                    }
                    var texts = _session.Run(tensors, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    return (texts, _session.Model);
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            { _metrics.Record(QuestVoiceMetric.Cancelled); Retire(); throw; }
            catch { Retire(); throw; }
        }
        finally { _gate.Release(); }
    }
    /// <summary>只在Run返回且拥有gate时废弃不可信原生资源。</summary>
    private void Retire()
    {
        var session = _session; _session = null;
        if (session is null) return;
        _metrics.Record(QuestVoiceMetric.Retired); session.Dispose();
    }
    /// <summary>重复释放无副作用；不在原生Run在途时释放。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _metrics.Record(QuestVoiceMetric.Closing);
        var start = Stopwatch.GetTimestamp();
        _gate.Wait();
        try { var session = _session; _session = null; session?.Dispose(); }
        finally { _metrics.Record(QuestVoiceMetric.Disposal, Stopwatch.GetTimestamp() - start); _gate.Release(); }
        // 不Dispose纯托管SemaphoreSlim：排队的WaitAsync必须醒来并观察关闭标记。
        // 所有引用结束后由GC回收；从不取内核WaitHandle，避免排队延续与释放竞态。
    }
}
