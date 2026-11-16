using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>
///     单个形状桶的推理会话：内置单用途会话池 + blob 名懒解析 + 单输入单输出推理 + 复用的输入缓冲。
///     det / rec / yolo 三条推理路径共用的最小单元，取代各自重复的 SessionEntry 模式。
///     输入名不必真实：blob 解析完成后按会话内的真实名覆盖 <see cref="NcnnInput.Name" />。
///     <para>
///         内置池持有 1–<see cref="NcnnTuningSettings.MaxParallelSessions" /> 个 <c>ncnn::Net</c> 句柄，
///         按需创建、用后归还，供 <c>Parallel.ForEach</c> 并行推理；默认并行数为 1，
///         此时行为与历史单会话完全一致，只有配置大于 1 才会创建额外会话。
///         所有会话共享同一个 <see cref="INcnnShim" />（库句柄在进程内缓存，只有 net 句柄按会话区分）。
///     </para>
/// </summary>
internal sealed class NcnnSession : IDisposable
{
    private const string DefaultInputName = "in0";
    private const string DefaultOutputName = "out0";

    private readonly INcnnShim _shim;
    private readonly NcnnNetOptions _template;
    private readonly int _maxSessions;
    private readonly SemaphoreSlim _gate;
    private readonly object _createLock = new();
    private readonly List<nint> _owned = [];
    private readonly Stack<nint> _idle = [];
    private bool _disposed;

    private string _inputName = DefaultInputName;
    private string _outputName = DefaultOutputName;
    private bool _blobsResolved;

    public NcnnSession(INcnnShim shim, NcnnNetOptions options, int maxSessions, int inputBufferSize)
    {
        _shim = shim ?? throw new ArgumentNullException(nameof(shim));
        _template = options ?? throw new ArgumentNullException(nameof(options));
        _maxSessions = Math.Clamp(maxSessions, 1, NcnnTuningSettings.MaxParallelSessions);
        _gate = new SemaphoreSlim(_maxSessions, _maxSessions);
        InputBuffer = new float[inputBufferSize];
    }

    /// <summary>复用的模型输入缓冲（CHW fp32），调用方写入后作为输入传给 <see cref="Infer" />。</summary>
    public float[] InputBuffer { get; }

    /// <summary>单输入单输出推理；首次推理时从模型解析真实 blob 名（解析不到保留 in0 / out0）。</summary>
    public NcnnOutput Infer(NcnnInput input)
    {
        return Use(net =>
        {
            if (!_blobsResolved) ResolveBlobs(net);
            var outputs = _shim.InferEx(net, [input with { Name = _inputName }], [_outputName]);
            if (outputs.Count == 0) throw new InvalidOperationException("ncnn 未返回推理输出。");
            return outputs[0];
        });
    }

    /// <summary>静态工厂：拼模型路径 → 校验产物存在（缺失抛 <see cref="FileNotFoundException" />）→ 创建会话；det / rec 装配共用。</summary>
    public static NcnnSession Create(INcnnShim shim, string modelDir, string paramFile, string modelFile,
        NcnnTuningSettings tuning, int gpuIndex, int inputBufferSize, string missingModelMessage)
    {
        var paramPath = Path.Combine(modelDir, paramFile);
        var modelPath = Path.Combine(modelDir, modelFile);
        if (!File.Exists(paramPath) || !File.Exists(modelPath))
            throw new FileNotFoundException(missingModelMessage, paramPath);
        return new NcnnSession(shim, tuning.ToNetOptions(paramPath, modelPath, gpuIndex),
            tuning.ParallelSessions, inputBufferSize);
    }

    private void ResolveBlobs(nint net)
    {
        var inputs = _shim.QueryInputBlobs(net);
        var outputs = _shim.QueryOutputBlobs(net);
        if (inputs.Length > 0 && !string.IsNullOrWhiteSpace(inputs[0])) _inputName = inputs[0];
        if (outputs.Length > 0 && !string.IsNullOrWhiteSpace(outputs[0])) _outputName = outputs[0];
        _blobsResolved = true;
    }

    /// <summary>在租约内执行一次同步操作，保证异常路径也会归还会话。</summary>
    private T Use<T>(Func<nint, T> action)
    {
        var net = Rent();
        try
        {
            return action(net);
        }
        finally
        {
            Return(net);
        }
    }

    /// <summary>租用一个会话；并行数用尽时阻塞等待归还。</summary>
    private nint Rent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gate.Wait();
        try
        {
            lock (_createLock)
            {
                if (_idle.Count > 0) return _idle.Pop();
            }

            return CreateNet();
        }
        catch
        {
            _gate.Release();
            throw;
        }
    }

    /// <summary>归还会话；已释放的会话直接销毁句柄。</summary>
    private void Return(nint net)
    {
        if (net == nint.Zero)
        {
            _gate.Release();
            return;
        }

        // Dispose 时已统一销毁全部句柄（含租出中的），归还路径不能再销毁一次。
        if (!_disposed)
        {
            lock (_createLock) _idle.Push(net);
        }

        _gate.Release();
    }

    private nint CreateNet()
    {
        var net = _shim.Create(_template);
        if (net == nint.Zero)
            throw new InvalidOperationException(
                $"ncnn 会话创建失败（{Path.GetFileName(_template.ParamPath)}，并行上限 {_maxSessions}）");

        lock (_createLock) _owned.Add(net);
        return net;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        List<nint> owned;
        lock (_createLock)
        {
            owned = [.. _owned];
            _owned.Clear();
            _idle.Clear();
        }

        foreach (var net in owned)
        {
            if (net != nint.Zero) _shim.Destroy(net);
        }

        // 故意不 Dispose 信号量：租约可能在会话释放后归还，Release 需要它仍然可用（SemaphoreSlim 无句柄，随 GC 回收）。
    }
}
