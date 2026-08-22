using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace BetterGenshinImpact.Service.Mcp;

[McpServerToolType]
public sealed class McpCommandTools(McpCommandCatalog commandCatalog)
{
    [McpServerTool(Name = "bgi_list_commands", ReadOnly = true, Idempotent = true), Description("列出 BetterGI 已注册 ViewModel 的全部 RelayCommand/function calling 命令及参数类型。")]
    public IReadOnlyList<McpCommandDescriptor> ListCommands(
        [Description("可选的命令名、ViewModel 名或属性名过滤词。")]
        string? filter = null,
        [Description("是否包含删除、重置、覆盖、退出、更新等需要确认的命令。")]
        bool includeDangerous = false) =>
        commandCatalog.List(filter, includeDangerous);

    [McpServerTool(Name = "bgi_invoke_command", OpenWorld = true), Description("调用命令目录中的一个 RelayCommand。优先使用专用 MCP 工具；此工具用于覆盖其余现有 UI 功能。")]
    public Task<McpCommandInvocationResult> InvokeCommand(
        [Description("由 bgi_list_commands 返回的完整名称，例如 home.on_start_trigger。")]
        string command,
        [Description("命令参数的 JSON 值；无参数命令请省略。")]
        JsonElement? argument = null,
        [Description("危险命令必须明确设为 true。")]
        bool confirm = false,
        CancellationToken cancellationToken = default) =>
        commandCatalog.InvokeAsync(command, argument, confirm, cancellationToken);
}
