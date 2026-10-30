using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

/// <summary>
/// 自动配音使用的 OCR：直接调用 BetterGI 全局 OCR 服务（<see cref="OcrFactory.PaddleOcr"/>）的无检测逐行识别。
/// 模型跟随“设置 → OCR 配置 → PaddleOCR模型”和游戏语言，推理设备跟随“硬件加速”与“OCR 使用 CPU”；
/// 不另建模型实例，全局设置变化后下一次识别即使用新模型。
/// </summary>
public sealed class QuestVoiceOcrEngine : IOcrEngine, IDisposable
{
    /// <summary>串行化本轮识别，并让释放等待在途识别结束。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>全局 OCR 工厂；每次识别都取当前实例，切换模型后自动使用新实例。</summary>
    private readonly OcrFactory _ocr;
    /// <summary>全局推理运行时，只用于诊断显示实际设备。</summary>
    private readonly InferenceRuntimeService _runtime;
    /// <summary>本轮低开销阶段计数。</summary>
    private readonly QuestVoiceMetrics _metrics;
    /// <summary>先标记关闭再排空在途识别。</summary>
    private int _disposed;

    /// <summary>注入全局 OCR 工厂和推理运行时；本类不拥有它们的生命周期。</summary>
    public QuestVoiceOcrEngine(OcrFactory ocr, InferenceRuntimeService runtime, QuestVoiceMetrics? metrics = null)
    {
        _ocr = ocr;
        _runtime = runtime;
        _metrics = metrics ?? new();
    }

    /// <summary>按输入顺序批量识别同一帧的所有行，一次推理完成；置信度为每个输出字符最大概率的平均值。</summary>
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
                var start = Stopwatch.GetTimestamp();
                OcrText[] texts;
                try
                {
                    texts = Recognize(lines.ToArray()).Select(result => new OcrText(result.Text, result.Score)).ToArray();
                }
                catch (Exception exception) when (exception is not (OperationCanceledException or OcrException))
                {
                    throw new OcrException(OcrErrorCode.Inference, "全局 OCR 识别失败：" + exception.Message, exception);
                }
                // 包含等待全局推理锁的时间：全局 OCR 由多个功能共用。
                finally { _metrics.Record(QuestVoiceMetric.NativeRun, Stopwatch.GetTimestamp() - start); }
                cancellationToken.ThrowIfCancellationRequested();
                return ((IReadOnlyList<OcrText>)texts, Describe());
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { _metrics.Record(QuestVoiceMetric.Cancelled); throw; }
        finally { _gate.Release(); }
    }

    /// <summary>用当前全局 OCR 识别；全局服务因切换模型或游戏语言被卸载时，换用新实例重试一次。</summary>
    private OcrRecognizerResult[] Recognize(Mat[] lines)
    {
        try { return Service().OcrResultLinesWithoutDetector(lines); }
        catch (ObjectDisposedException) { return Service().OcrResultLinesWithoutDetector(lines); }
    }

    /// <summary>取得全局 OCR 实例；首次使用时由全局工厂加载模型，失败时转换为模型初始化错误。</summary>
    private IOcrService Service()
    {
        try { return _ocr.PaddleOcr; }
        catch (Exception exception)
        {
            throw new OcrException(OcrErrorCode.ModelInitialization, "全局 OCR 初始化失败：" + exception.Message, exception);
        }
    }

    /// <summary>诊断显示当前全局 OCR 的识别模型和推理设备。</summary>
    private OcrModelInfo Describe() => new(_ocr.ActiveModelType.RecognitionModel.Name, _runtime.Current.OcrDescription);

    /// <summary>重复释放无副作用；只等待本轮在途识别结束，不释放全局 OCR。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _gate.Wait();
        _gate.Release();
        // 不释放纯托管 SemaphoreSlim：排队的 WaitAsync 必须醒来并观察关闭标记。
    }
}
