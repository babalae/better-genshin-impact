using System;
using System.Diagnostics;
using BetterGenshinImpact.GameTask.Runtime;

namespace BetterGenshinImpact.GameTask.Common.Job;

// 每次运行的每个路线点独立记录。只有这个点明确要求演奏才会创建。
internal sealed class InstrumentQuestRecovery
{
    private GameRuntime? _runtime;
    private long _exitedAt;

    internal void RecordExit(GameRuntime runtime)
    {
        _runtime = runtime;
        _exitedAt = Stopwatch.GetTimestamp();
    }

    internal bool IsReentry(GameRuntime? runtime) => _runtime != null && ReferenceEquals(_runtime, runtime)
        && Stopwatch.GetElapsedTime(_exitedAt) <= TimeSpan.FromSeconds(10);
}
