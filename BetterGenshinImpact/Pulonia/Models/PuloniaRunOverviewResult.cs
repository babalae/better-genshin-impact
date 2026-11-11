using System.Collections.Generic;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 一次跨实例运行列表读取的结果。
/// </summary>
/// <param name="Runs">合并后的运行，按提交时间倒序；同一运行 ID 只出现一次，已归档的以历史为准</param>
/// <param name="StateFailures">读不出而跳过的其他实例运行状态文件</param>
/// <param name="ChangeStamp">本次读取所依据的修改标记；之后标记不同才需要再次读取</param>
public sealed record PuloniaRunOverviewResult(
    IReadOnlyList<PuloniaTaskRunView> Runs,
    IReadOnlyList<PuloniaTaskFileLoadFailure> StateFailures,
    string ChangeStamp);
