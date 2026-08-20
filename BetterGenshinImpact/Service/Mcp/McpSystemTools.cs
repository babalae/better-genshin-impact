using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Core.Script;
using ModelContextProtocol.Server;

namespace BetterGenshinImpact.Service.Mcp;

[McpServerToolType]
public sealed class McpSystemTools
{
    [McpServerTool(Name = "bgi_ping", ReadOnly = true, Idempotent = true), Description("检查 BetterGI MCP 服务是否可用。")]
    public static object Ping() => new
    {
        ok = true,
        serverTime = DateTimeOffset.Now,
        protocol = "MCP Streamable HTTP",
    };

    [McpServerTool(Name = "bgi_get_status", ReadOnly = true, Idempotent = true),
     Description("获取 BetterGI、截图器、游戏窗口和独立任务的当前状态。")]
    public static object GetStatus()
    {
        var context = TaskContext.Instance();
        var currentProject = context.CurrentScriptProject;
        return new
        {
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
            processId = Environment.ProcessId,
            processName = Process.GetCurrentProcess().ProcessName,
            captureInitialized = context.IsInitialized,
            gameWindowHandle = context.GameHandle.ToInt64(),
            taskRunning = TaskControl.TaskSemaphore.CurrentCount == 0,
            cancellationRequested = CancellationContext.Instance.IsCancellationRequested,
            currentScript = currentProject is null
                ? null
                : new
                {
                    currentProject.Name,
                    currentProject.FolderName,
                    currentProject.Type,
                },
        };
    }

    [McpServerTool(Name = "bgi_cancel_current_task", Destructive = true, Idempotent = true),
     Description("请求取消当前独立任务或连续脚本任务。不会关闭 BetterGI。")]
    public static object CancelCurrentTask(
        [Description("必须明确设为 true，表示确认中止当前任务。")]
        bool confirm)
    {
        if (!confirm)
        {
            throw new InvalidOperationException("取消任务需要将 confirm 设为 true。");
        }

        var wasCancellationRequested = CancellationContext.Instance.IsCancellationRequested;
        CancellationContext.Instance.ManualCancel();
        return new
        {
            accepted = true,
            alreadyRequested = wasCancellationRequested,
        };
    }
}