using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>
///     ncnn_shim.dll 的会话级互操作抽象：生产实现是 <see cref="NcnnShim" />（动态加载原生库），
///     单元测试注入纯托管替身即可覆盖会话池、blob 解析与推理转发逻辑，无需原生 DLL。
///     实现不要求线程安全：调用方通过 <see cref="NcnnSessionPool" /> 串行化单个会话句柄。
/// </summary>
public interface INcnnShim : IDisposable
{
    /// <summary>创建会话：全部参数经由版本化结构下发，且在 load_param 之前写入。</summary>
    nint Create(NcnnNetOptions options);

    void Destroy(nint handle);

    /// <summary>
    ///     多输入多输出推理：输出带 dims/w/h/c/elembits/cstep，数据已拷成紧凑 fp32。
    /// </summary>
    IReadOnlyList<NcnnOutput> InferEx(nint handle,
        IReadOnlyList<NcnnInput> inputs, IReadOnlyList<string> outputNames,
        NcnnExtractorOptions? extractor = null);

    /// <summary>读取会话的输入 blob 名列表；读不到的槽位为空串。</summary>
    string[] QueryInputBlobs(nint handle);

    /// <summary>读取会话的输出 blob 名列表；读不到的槽位为空串。</summary>
    string[] QueryOutputBlobs(nint handle);
}
