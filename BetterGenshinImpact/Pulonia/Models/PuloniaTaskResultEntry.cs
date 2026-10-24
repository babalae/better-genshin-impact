using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 节点结果字段的解释方式；界面据此选择展示文本，不把缺失值当成 false、0 或空输出。
/// </summary>
public enum PuloniaTaskResultEntryKind
{
    /// <summary>按执行器返回的原值展示。</summary>
    Value,

    /// <summary>执行器声明的关键字段没有返回，可能在结果产生前失败或取消。</summary>
    NotReturned,

    /// <summary>执行器没有统计该项。</summary>
    NotCounted,

    /// <summary>执行器确认成功，没有失败原因。</summary>
    NoFailure,

    /// <summary>执行失败，但执行器没有提供失败原因。</summary>
    FailureNotProvided,

    /// <summary>奖励汇总：值为每项带 name、可选 count 的数组，空数组表示没有识别到奖励。</summary>
    Rewards,

    /// <summary>路线拾取统计：值为“物品名 → 数量”的对象，空对象表示已统计但未拾取。</summary>
    CollectedItems
}

/// <summary>
/// 按任务类型从节点结果中解释出的一项字段。
/// </summary>
/// <param name="Label">中文标签。</param>
/// <param name="Kind">解释方式。</param>
/// <param name="Value">原始值；只有 <see cref="PuloniaTaskResultEntryKind.Value"/>、Rewards 和 CollectedItems 携带。</param>
public sealed record PuloniaTaskResultEntry(string Label, PuloniaTaskResultEntryKind Kind, JToken? Value = null);
