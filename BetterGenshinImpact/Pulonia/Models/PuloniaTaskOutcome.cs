using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 具体任务执行器返回的结构化结果。
/// </summary>
public sealed class PuloniaTaskOutcome
{
    /// <summary>
    /// 本次动作的业务完成程度。
    /// </summary>
    public PuloniaTaskOutcomeKind Kind { get; }

    /// <summary>
    /// 本次动作是否成功完成。
    /// </summary>
    public bool IsSuccess => Kind is PuloniaTaskOutcomeKind.Succeeded
        or PuloniaTaskOutcomeKind.ExecutedUnverified;

    /// <summary>
    /// 面向调用端的结果原因或摘要。
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// 执行器附带的结构化结果副本。
    /// </summary>
    public JObject Data { get; }

    /// <summary>
    /// 执行器随最终结果返回的结构化证据。
    /// </summary>
    public IReadOnlyList<PuloniaTaskEvidence> Evidence { get; }

    /// <summary>
    /// 建立不可变执行结果，并复制调用方提供的数据。
    /// </summary>
    public PuloniaTaskOutcome(PuloniaTaskOutcomeKind kind, string message, JObject? data = null,
        IEnumerable<PuloniaTaskEvidence>? evidence = null)
    {
        Kind = kind;
        Message = message;
        Data = data is null ? new JObject() : (JObject)data.DeepClone();
        Evidence = new ReadOnlyCollection<PuloniaTaskEvidence>((evidence ?? []).ToArray());
    }

    /// <summary>
    /// 建立成功结果。
    /// </summary>
    public static PuloniaTaskOutcome Success(string message, JObject? data = null,
        IEnumerable<PuloniaTaskEvidence>? evidence = null)
        => new(PuloniaTaskOutcomeKind.Succeeded, message, data, evidence);

    /// <summary>
    /// 建立“执行结束但副作用未核验”的结果。
    /// </summary>
    public static PuloniaTaskOutcome ExecutedUnverified(string message, JObject? data = null,
        IEnumerable<PuloniaTaskEvidence>? evidence = null)
        => new(PuloniaTaskOutcomeKind.ExecutedUnverified, message, data, evidence);

    /// <summary>
    /// 建立只确认部分副作用的结果。
    /// </summary>
    public static PuloniaTaskOutcome Partial(string message, JObject? data = null,
        IEnumerable<PuloniaTaskEvidence>? evidence = null)
        => new(PuloniaTaskOutcomeKind.PartiallySucceeded, message, data, evidence);

    /// <summary>
    /// 建立失败结果。
    /// </summary>
    public static PuloniaTaskOutcome Failure(string message, JObject? data = null,
        IEnumerable<PuloniaTaskEvidence>? evidence = null)
        => new(PuloniaTaskOutcomeKind.Failed, message, data, evidence);
}
