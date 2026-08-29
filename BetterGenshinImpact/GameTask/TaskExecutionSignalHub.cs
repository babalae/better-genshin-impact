using System.Collections.Concurrent;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 在调用链中传递执行 ID，并由 TaskRunner 在真正取得独立任务锁时发出一次性启动信号。
/// AsyncLocal 会随 Task/Dispatcher ExecutionContext 流动，不会把其他并发任务误认为本次启动。
/// </summary>
public static class TaskExecutionSignalHub
{
    private static readonly AsyncLocal<string?> CurrentExecutionId = new();
    private static readonly ConcurrentDictionary<string, TaskCompletionSource<DateTimeOffset>> RunningSignals = new();

    public static Task<DateTimeOffset> Register(string executionId)
    {
        var signal = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!RunningSignals.TryAdd(executionId, signal))
            throw new InvalidOperationException($"执行 ID 已注册：{executionId}");
        return signal.Task;
    }

    public static IDisposable Enter(string executionId)
    {
        var previous = CurrentExecutionId.Value;
        CurrentExecutionId.Value = executionId;
        return new ExecutionScope(previous);
    }

    public static void SignalRunning()
    {
        var executionId = CurrentExecutionId.Value;
        if (executionId is not null && RunningSignals.TryGetValue(executionId, out var signal))
            signal.TrySetResult(DateTimeOffset.Now);
    }

    public static void Unregister(string executionId) => RunningSignals.TryRemove(executionId, out _);

    private sealed class ExecutionScope(string? previous) : IDisposable
    {
        public void Dispose() => CurrentExecutionId.Value = previous;
    }
}
