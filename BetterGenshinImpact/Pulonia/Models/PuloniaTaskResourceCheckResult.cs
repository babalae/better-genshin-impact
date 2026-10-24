using System;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 一轮批量资源检查中单个任务的结果：读取成功时 <see cref="State"/> 有值，资源读取失败时 <see cref="Error"/> 有值。
/// </summary>
public sealed class PuloniaTaskResourceCheckResult
{
    /// <summary>读取成功时的资源状态；失败时为 null。</summary>
    public PuloniaTaskResourceVersionState? State { get; }

    /// <summary>资源读取失败的原因；成功时为 null。</summary>
    public Exception? Error { get; }

    /// <summary>建立一份成功结果。</summary>
    public PuloniaTaskResourceCheckResult(PuloniaTaskResourceVersionState state) => State = state;

    /// <summary>建立一份失败结果。</summary>
    public PuloniaTaskResourceCheckResult(Exception error) => Error = error;
}
