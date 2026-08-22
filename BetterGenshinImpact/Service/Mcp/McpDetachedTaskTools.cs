using System.ComponentModel;
using ModelContextProtocol.Server;

namespace BetterGenshinImpact.Service.Mcp;

[McpServerToolType]
public sealed class McpDetachedTaskTools(McpDetachedTaskRegistry registry)
{
    [McpServerTool(Name = "bgi_get_detached_task_status", ReadOnly = true, Idempotent = true), Description("查看由 Agent/MCP 脱离式启动的最近后台任务状态。running 表示 Agent 调用已结束但 BetterGI 任务仍运行；真正停止请调用 bgi_stop_current_task_and_wait。")]
    public IReadOnlyList<McpDetachedTaskEntry> GetDetachedTaskStatus(
        [Description("返回最近 1-100 条，默认 20。")]
        int limit = 20) => registry.GetEntries(limit);
}
