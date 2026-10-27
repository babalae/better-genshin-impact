using System;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>
///     单个形状桶的推理会话：会话池 + blob 名懒解析 + 单输入单输出推理 + 复用的输入缓冲。
///     det / rec / yolo 三条推理路径共用的最小单元，取代各自重复的 SessionEntry 模式。
///     输入名不必真实：blob 解析完成后按会话内的真实名覆盖 <see cref="NcnnInput.Name" />。
/// </summary>
internal sealed class NcnnSession : IDisposable
{
    private const string DefaultInputName = "in0";
    private const string DefaultOutputName = "out0";

    private readonly INcnnShim _shim;
    private readonly NcnnSessionPool _pool;
    private string _inputName = DefaultInputName;
    private string _outputName = DefaultOutputName;
    private bool _blobsResolved;

    public NcnnSession(INcnnShim shim, NcnnNetOptions options, int maxSessions, int inputBufferSize)
    {
        _shim = shim ?? throw new ArgumentNullException(nameof(shim));
        _pool = new NcnnSessionPool(shim, options, maxSessions);
        InputBuffer = new float[inputBufferSize];
    }

    /// <summary>复用的模型输入缓冲（CHW fp32），调用方写入后作为输入传给 <see cref="Infer" />。</summary>
    public float[] InputBuffer { get; }

    /// <summary>单输入单输出推理；首次推理时从模型解析真实 blob 名（解析不到保留 in0 / out0）。</summary>
    public NcnnOutput Infer(NcnnInput input)
    {
        return _pool.Use(net =>
        {
            if (!_blobsResolved) ResolveBlobs(net);
            var outputs = _shim.InferEx(net, [input with { Name = _inputName }], [_outputName]);
            if (outputs.Count == 0) throw new InvalidOperationException("ncnn 未返回推理输出。");
            return outputs[0];
        });
    }

    private void ResolveBlobs(nint net)
    {
        var inputs = _shim.QueryInputBlobs(net);
        var outputs = _shim.QueryOutputBlobs(net);
        if (inputs.Length > 0 && !string.IsNullOrWhiteSpace(inputs[0])) _inputName = inputs[0];
        if (outputs.Length > 0 && !string.IsNullOrWhiteSpace(outputs[0])) _outputName = outputs[0];
        _blobsResolved = true;
    }

    public void Dispose() => _pool.Dispose();
}
