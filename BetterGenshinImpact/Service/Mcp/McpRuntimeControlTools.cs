using System.ComponentModel;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using ModelContextProtocol.Server;

namespace BetterGenshinImpact.Service.Mcp;

[McpServerToolType]
public sealed class McpRuntimeControlTools
{
    [McpServerTool(Name = "bgi_get_execution_status", ReadOnly = true, Idempotent = true),
     Description("获取当前 BetterGI 独立任务/调度脚本的详细执行状态，包括组、项目、类型、进度、循环、暂停、取消和自动拾取暂停计数。")]
    public static object GetExecutionStatus()
    {
        var taskContext = TaskContext.Instance();
        var runner = RunnerContext.Instance;
        var project = taskContext.CurrentScriptProject;
        var progress = runner.taskProgress;
        return new
        {
            running = TaskControl.TaskSemaphore.CurrentCount == 0,
            captureInitialized = taskContext.IsInitialized,
            cancellationRequested = CancellationContext.Instance.IsCancellationRequested,
            manualStopRequested = CancellationContext.Instance.IsManualStop,
            paused = runner.IsSuspend,
            runner.IsContinuousRunGroup,
            runner.IsPreExecution,
            runner.PartyName,
            runner.AutoPickTriggerStopCount,
            currentProject = project is null
                ? null
                : new
                {
                    groupName = project.GroupInfo?.Name,
                    project.Index,
                    project.Name,
                    project.FolderName,
                    project.Type,
                    project.Status,
                    project.Schedule,
                    project.RunNum,
                    project.AllowJsNotification,
                    allowJsHttp = project.Type == "Javascript" && project.AllowJsHTTP,
                },
            progress = progress is null
                ? null
                : new
                {
                    progress.Name,
                    progress.ScriptGroupNames,
                    progress.CurrentScriptGroupName,
                    progress.CurrentScriptGroupProjectInfo,
                    progress.LastScriptGroupName,
                    progress.LastSuccessScriptGroupProjectInfo,
                    progress.Loop,
                    progress.LoopCount,
                    progress.ConsecutiveFailureCount,
                    progress.StartTime,
                    progress.EndTime,
                },
        };
    }

    [McpServerTool(Name = "bgi_interrupt_current_script", Destructive = true, Idempotent = true),
     Description("明确中断当前调度项目（Javascript、Pathing、KeyMouse 或 Shell）及其所在任务。发送手动取消信号、解除暂停并释放模拟按键；可等待清理完成。")]
    public static async Task<object> InterruptCurrentScript(
        [Description("必须明确设为 true。")] bool confirm,
        [Description("等待任务释放互斥锁的秒数，0-300；0 表示只发送中断。")]
        int waitSeconds = 15,
        CancellationToken cancellationToken = default)
    {
        if (!confirm) throw new InvalidOperationException("中断当前脚本需要将 confirm 设为 true。");
        if (waitSeconds is < 0 or > 300) throw new ArgumentOutOfRangeException(nameof(waitSeconds));
        var project = TaskContext.Instance().CurrentScriptProject;
        if (project is null && TaskControl.TaskSemaphore.CurrentCount != 0)
            return new { requested = false, reason = "当前没有运行中的调度脚本或独立任务。", stopped = true };
        var snapshot = project is null
            ? null
            : new
            {
                groupName = project.GroupInfo?.Name, project.Index, project.Name, project.FolderName, project.Type
            };
        RequestManualStop();
        var stopped = waitSeconds == 0
            ? TaskControl.TaskSemaphore.CurrentCount != 0
            : await WaitUntilStopped(TimeSpan.FromSeconds(waitSeconds), cancellationToken);
        return new { requested = true, target = snapshot, stopped, timedOut = waitSeconds > 0 && !stopped };
    }

    [McpServerTool(Name = "bgi_stop_current_task_and_wait", Destructive = true, Idempotent = true),
     Description(
         "停止任何当前独立任务或连续配置组并等待 TaskRunner 完成 finally 清理。与取消 MCP 请求不同，本工具会真正触发 CancellationContext.ManualCancel。")]
    public static async Task<object> StopCurrentTaskAndWait(
        [Description("必须明确设为 true。")] bool confirm,
        [Description("等待清理完成的秒数，1-300。超时只返回 stopped=false，不会强杀线程。")]
        int timeoutSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        if (!confirm) throw new InvalidOperationException("停止当前任务需要将 confirm 设为 true。");
        if (timeoutSeconds is < 1 or > 300) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        var wasRunning = TaskControl.TaskSemaphore.CurrentCount == 0;
        RequestManualStop();
        var stopped = !wasRunning || await WaitUntilStopped(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
        return new { requested = wasRunning, stopped, timedOut = wasRunning && !stopped, timeoutSeconds };
    }

    [McpServerTool(Name = "bgi_wait_for_current_task", ReadOnly = true, Idempotent = true),
     Description("显式等待当前 BetterGI 独立任务自然结束，不发送取消。主要供外部客户端使用；Agent 启动任务后通常不应调用，以免浪费上下文。")]
    public static async Task<object> WaitForCurrentTask(
        [Description("最长等待秒数，1-3600。")] int timeoutSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        if (timeoutSeconds is < 1 or > 3600) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        var wasRunning = TaskControl.TaskSemaphore.CurrentCount == 0;
        var completed = !wasRunning || await WaitUntilStopped(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
        return new { wasRunning, completed, timedOut = wasRunning && !completed, timeoutSeconds };
    }

    [McpServerTool(Name = "bgi_pause_current_task", Destructive = true, Idempotent = true),
     Description("请求协作式暂停当前任务。内置路径和会调用 TaskControl 的脚本会在安全检查点暂停；第三方 JS 的纯计算或外部等待不保证立即响应。")]
    public static object PauseCurrentTask([Description("必须明确设为 true。")] bool confirm)
    {
        if (!confirm) throw new InvalidOperationException("暂停当前任务需要将 confirm 设为 true。");
        if (TaskControl.TaskSemaphore.CurrentCount != 0) return new { changed = false, reason = "当前没有运行中的独立任务。" };
        var wasPaused = RunnerContext.Instance.IsSuspend;
        RunnerContext.Instance.IsSuspend = true;
        return new { changed = !wasPaused, paused = true };
    }

    [McpServerTool(Name = "bgi_resume_current_task", Destructive = true, Idempotent = true),
     Description("解除 BetterGI 当前任务的协作式暂停。")]
    public static object ResumeCurrentTask([Description("必须明确设为 true。")] bool confirm)
    {
        if (!confirm) throw new InvalidOperationException("恢复当前任务需要将 confirm 设为 true。");
        var wasPaused = RunnerContext.Instance.IsSuspend;
        RunnerContext.Instance.IsSuspend = false;
        return new { changed = wasPaused, paused = false };
    }

    [McpServerTool(Name = "bgi_release_all_simulated_keys", Destructive = true, Idempotent = true),
     Description("紧急释放 BetterGI 模拟器当前可能按住的全部键盘/鼠标按键。不会取消任务；任务仍运行时可能再次按键。")]
    public static object ReleaseAllSimulatedKeys([Description("必须明确设为 true。")] bool confirm)
    {
        if (!confirm) throw new InvalidOperationException("释放模拟按键需要将 confirm 设为 true。");
        Simulation.ReleaseAllKey();
        return new { released = true, taskStillRunning = TaskControl.TaskSemaphore.CurrentCount == 0 };
    }

    private static void RequestManualStop()
    {
        RunnerContext.Instance.IsSuspend = false;
        CancellationContext.Instance.ManualCancel();
        Simulation.ReleaseAllKey();
    }

    private static async Task<bool> WaitUntilStopped(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (TaskControl.TaskSemaphore.CurrentCount == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, cancellationToken);
        }

        return TaskControl.TaskSemaphore.CurrentCount != 0;
    }
}