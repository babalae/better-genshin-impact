using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

/// <summary>
/// 自动配音使用的 OCR：直接调用 BetterGI 内置的 PP-OCRv6 识别组件 <see cref="Rec"/>。
/// 会话由 BgiOnnxFactory 创建，推理设备跟随“硬件加速”与“OCR 使用 CPU”设置；本轮独占一个实例，不与全局 OCR 共用推理锁。
/// </summary>
public sealed class QuestVoiceOcrEngine : IOcrEngine, IDisposable
{
    /// <summary>串行化识别与释放，同一识别实例不并发推理。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>首次识别时才创建识别组件，避免启动配音就加载模型。</summary>
    private readonly Func<Rec> _create;
    /// <summary>本轮低开销阶段计数。</summary>
    private readonly QuestVoiceMetrics _metrics;
    /// <summary>诊断显示用的模型与设备描述。</summary>
    private readonly OcrModelInfo _model;
    /// <summary>本轮识别组件；只在持有 _gate 时访问。</summary>
    private Rec? _rec;
    /// <summary>先标记关闭再排空在途识别。</summary>
    private int _disposed;

    /// <summary>注入识别组件的创建方式，测试可替换。</summary>
    public QuestVoiceOcrEngine(Func<Rec> create, string device, QuestVoiceMetrics? metrics = null)
    {
        _create = create;
        _metrics = metrics ?? new();
        _model = new("PP-OCRv6（内置）", device);
    }

    /// <summary>按输入顺序识别每一行；置信度为每个字符最大概率的平均值，与原自带解码一致。</summary>
    public async Task<(IReadOnlyList<OcrText> Texts, OcrModelInfo Model)> RecognizeAsync(
        IReadOnlyList<Mat> lines, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rec = _rec ??= Create();
                var start = Stopwatch.GetTimestamp();
                OcrText[] texts;
                try
                {
                    texts = rec.Run(lines.ToArray()).Select(result => new OcrText(result.Text, result.Score)).ToArray();
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    throw new OcrException(OcrErrorCode.Inference, "内置 OCR 推理失败：" + exception.Message, exception);
                }
                finally { _metrics.Record(QuestVoiceMetric.NativeRun, Stopwatch.GetTimestamp() - start); }
                cancellationToken.ThrowIfCancellationRequested();
                return ((IReadOnlyList<OcrText>)texts, _model);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { _metrics.Record(QuestVoiceMetric.Cancelled); throw; }
        finally { _gate.Release(); }
    }

    /// <summary>创建失败时转换为模型初始化错误，界面据此提示检查 OCR。</summary>
    private Rec Create()
    {
        var start = Stopwatch.GetTimestamp();
        try { return _create(); }
        catch (Exception exception)
        {
            throw new OcrException(OcrErrorCode.ModelInitialization, "内置 OCR 初始化失败：" + exception.Message, exception);
        }
        finally { _metrics.Record(QuestVoiceMetric.Initialization, Stopwatch.GetTimestamp() - start); }
    }

    /// <summary>重复释放无副作用；等待在途识别结束后再释放原生会话。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _gate.Wait();
        try { _rec?.Dispose(); _rec = null; }
        finally { _gate.Release(); }
        // 不释放纯托管 SemaphoreSlim：排队的 WaitAsync 必须醒来并观察关闭标记。
    }
}
