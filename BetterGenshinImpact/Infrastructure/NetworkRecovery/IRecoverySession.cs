using System;

namespace BetterGenshinImpact.Infrastructure.NetworkRecovery;

public readonly record struct TaskExecutionContext(
    string FlowName,
    string TaskId,
    string TaskName,
    int TaskIndex,
    string? ScriptGroupName = null,
    int? ScriptProjectIndex = null);

public interface IRecoverySession
{
    TaskExecutionContext? CurrentTask { get; }
    bool IsRecovering { get; }
    bool IsCurrentRecoveryExecution { get; }
    void BeginTask(TaskExecutionContext context);
    void CompleteTask(string taskId);
    IDisposable? BeginRecovery();
    void Clear();
}
