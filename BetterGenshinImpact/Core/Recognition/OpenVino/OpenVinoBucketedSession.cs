using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BetterGenshinImpact.Core.Recognition.OpenVino;

/// <summary>
///     宽度分桶的静态形状 OCR 会话：把识别模型的动态宽度收敛到少量固定宽度桶，
///     每次推理选「最小够用桶」，不足部分由 <see cref="OpenVinoSession" /> 补零。
///     GPU 动态形状是预览特性（每个新宽度首次推理触发秒级 kernel 编译），NPU 编译器只接受静态图；
///     分桶后每个宽度至多编译一次并经 <see cref="OpenVinoCompiledModelCache" /> 进程级复用。
///     桶按首次使用懒编译；超出最大桶的输入应由 Rec 预处理先行压缩（FixedInputWidth 即最大桶）。
/// </summary>
internal sealed class OpenVinoBucketedSession : IOcrInferenceSession
{
    private readonly string _modelPath;
    private readonly string _deviceId;
    private readonly bool _enableCache;
    private readonly int _height;
    private readonly int[] _widths;
    private readonly Dictionary<int, OpenVinoSession> _sessions = [];
    private readonly object _lock = new();
    private bool _disposed;

    /// <param name="height">模型输入高度（PP-OCR 识别模型为 48）。</param>
    /// <param name="widths">宽度桶集合；构造时去重升序，必须非空且均在合法区间内。</param>
    public OpenVinoBucketedSession(string modelPath, string deviceId, bool enableCache, int height,
        IReadOnlyList<int> widths)
    {
        ArgumentNullException.ThrowIfNull(widths);
        var sorted = widths.Distinct().OrderBy(w => w).ToArray();
        if (sorted.Length == 0)
            throw new ArgumentException("宽度桶不能为空；需要动态宽度时请直接使用 OpenVinoSession。", nameof(widths));
        if (sorted.Any(w => w is < OpenVinoBackendSettings.MinStaticRecWidth or > OpenVinoBackendSettings.MaxStaticRecWidth))
            throw new ArgumentException(
                $"宽度桶须在 {OpenVinoBackendSettings.MinStaticRecWidth}–{OpenVinoBackendSettings.MaxStaticRecWidth} 区间内。",
                nameof(widths));
        _modelPath = modelPath;
        _deviceId = deviceId;
        _enableCache = enableCache;
        _height = height;
        _widths = sorted;
    }

    /// <summary>输入节点名：取最小桶会话的名称（各桶来自同一模型，名称一致）。</summary>
    public string InputName => GetSession(_widths[0]).InputName;

    /// <summary>最大桶宽度：Rec 预处理据此把超宽文本行压缩到最大桶以内。</summary>
    public int? FixedInputWidth => _widths[^1];

    /// <summary>模型输入高度（各桶一致）。</summary>
    public int? FixedInputHeight => _height;

    public OcrInferenceResult[] Run(Tensor<float> input)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var width = input.Dimensions[^1];
            var bucket = _widths.FirstOrDefault(w => w >= width);
            if (bucket == 0)
                throw new InvalidOperationException(
                    $"输入宽度 {width} 超出最大宽度桶 {_widths[^1]}；Rec 预处理应先压缩到固定宽度以内");
            return GetSession(bucket).Run(input);
        }
    }

    /// <summary>取（或懒编译）指定宽度桶的会话。调用方须持有 <see cref="_lock" />。</summary>
    private OpenVinoSession GetSession(int width)
    {
        if (_sessions.TryGetValue(width, out var session)) return session;
        session = new OpenVinoSession(_modelPath, _deviceId, _enableCache, [1, 3, _height, width]);
        _sessions[width] = session;
        return session;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var session in _sessions.Values) session.Dispose();
            _sessions.Clear();
        }
    }
}