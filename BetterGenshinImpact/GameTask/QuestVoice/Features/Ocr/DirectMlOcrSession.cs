using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

/// <summary>独占原生会话与不变元数据；调用者负责串行与排空。</summary>
internal sealed class DirectMlOcrSession : IDirectMlOcrSession
{
    /// <summary>顺序执行、禁用memory pattern的DirectML资源。</summary>
    private readonly InferenceSession _session;
    /// <summary>初始化一次的词表。</summary>
    private readonly string[] _tokens;
    /// <summary>本轮计数器。</summary>
    private readonly QuestVoiceMetrics _metrics;
    /// <summary>是否已结束首次profile并验证DML节点。</summary>
    private bool _verified;
    /// <summary>防止重复释放原生资源。</summary>
    private bool _disposed;
    /// <summary>模型哈希与首次真实执行的provider证据。</summary>
    public OcrModelInfo Model { get; private set; }

    /// <summary>CreateSession：初始化模型、字典与会话；失败时释放部分资源。</summary>
    internal DirectMlOcrSession(OcrSettings settings, string profileDirectory, QuestVoiceMetrics metrics)
    {
        _metrics = metrics;
        if (!File.Exists(settings.ModelPath)) throw new OcrException(OcrErrorCode.ModelMissing, $"OCR 模型不存在：{settings.ModelPath}");
        byte[] model;
        try { model = File.ReadAllBytes(settings.ModelPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { throw new OcrException(OcrErrorCode.ModelInitialization, $"无法读取 OCR 模型：{exception.Message}", exception); }
        try
        {
            using var options = new SessionOptions { EnableMemoryPattern = false, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
            Directory.CreateDirectory(profileDirectory);
            options.ProfileOutputPathPrefix = Path.Combine(profileDirectory, $"ocr-{Guid.NewGuid():N}");
            options.EnableProfiling = true;
            options.AppendExecutionProvider_DML(settings.DeviceId);
            _session = new InferenceSession(model, options);
        }
        catch (Exception exception)
        { throw new OcrException(OcrErrorCode.ModelInitialization, $"DirectML OCR 初始化失败（没有 CPU 回退）：{exception.Message}", exception); }
        try
        {
            var metadata = _session.ModelMetadata.CustomMetadataMap;
            if (!metadata.TryGetValue("character", out var dictionary))
                throw new OcrException(OcrErrorCode.ModelInitialization, "OCR 模型缺少 character 字典。");
            var characters = dictionary.TrimEnd('\r', '\n').Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            _tokens = new[] { "" }.Concat(characters).Append(" ").ToArray();
            if (characters.Length != 18708 || _tokens.Any(t => t.Contains('\r'))
                || !_session.InputMetadata.TryGetValue("x", out var input) || input.Dimensions.Length != 4
                || input.Dimensions[1] != 3 || input.Dimensions[2] != 48
                || !_session.OutputMetadata.TryGetValue("fetch_name_0", out var output)
                || output.Dimensions.Length != 3 || output.Dimensions[2] != _tokens.Length)
                throw new OcrException(OcrErrorCode.ModelInitialization, "需要带 18708 token 字典的 PP-OCRv6 Rec 模型（x: N,3,48,W）。");
            Model = new(settings.ModelPath, Convert.ToHexString(SHA256.HashData(model)).ToLowerInvariant(),
                "DmlExecutionProvider", settings.DeviceId, null);
        }
        catch { _session.Dispose(); throw; }
    }

    /// <summary>Run：每批独立RunOptions，不把Terminate传给下一批。</summary>
    public IReadOnlyList<OcrText> Run(IReadOnlyList<OcrTensor> tensors, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            token.ThrowIfCancellationRequested();
            var texts = new List<OcrText>();
            // 停止不只丢弃识别结果，也请求 ONNX 终止本轮原生推理。
            using var runOptions = new RunOptions();
            using var cancellation = token.Register(() => runOptions.Terminate = true);
            foreach (var tensor in tensors)
            {
                token.ThrowIfCancellationRequested();
                var inputTensor = new DenseTensor<float>(tensor.Data, new[] { 1, 3, 48, tensor.Width });
                _metrics.Record(QuestVoiceMetric.NativeRunStarted);
                var start = Stopwatch.GetTimestamp();
                using var values = RunNative(inputTensor, runOptions, start);
                token.ThrowIfCancellationRequested();
                var result = values[0].AsTensor<float>();
                if (result.Dimensions.Length != 3 || result.Dimensions[0] != 1 || result.Dimensions[2] != _tokens.Length)
                    throw new OcrException(OcrErrorCode.Inference, "OCR 推理输出形状错误。");
                start = Stopwatch.GetTimestamp();
                try { texts.Add(CtcDecoder.Decode(result.ToArray(), result.Dimensions[1], _tokens)); }
                finally { _metrics.Record(QuestVoiceMetric.Ctc, Stopwatch.GetTimestamp() - start); }
            }
            if (!_verified)
            {
                var profile = Path.GetFullPath(_session.EndProfiling());
                var counts = OcrExecutionProfile.ReadAsync(profile, token).GetAwaiter().GetResult();
                Model = Model with { ProfilePath = profile, KernelCounts = counts };
                _verified = true;
            }
            token.ThrowIfCancellationRequested();
            return texts;
        }
        catch (OnnxRuntimeException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        catch (OnnxRuntimeException exception)
        { throw new OcrException(OcrErrorCode.Inference, $"DirectML OCR 推理失败：{exception.Message}", exception); }
    }

    /// <summary>原生Run墙钟含CPU/EP/同步开销，不能称为纯GPU核函数时间。</summary>
    private IDisposableReadOnlyCollection<DisposableNamedOnnxValue> RunNative(DenseTensor<float> tensor, RunOptions options, long start)
    {
        try { return _session.Run([NamedOnnxValue.CreateFromTensor("x", tensor)], ["fetch_name_0"], options); }
        finally { _metrics.Record(QuestVoiceMetric.NativeRun, Stopwatch.GetTimestamp() - start); }
    }

    /// <summary>释放本实例拥有的资源；引擎保证没有在途Run。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _session.Dispose();
    }
}
