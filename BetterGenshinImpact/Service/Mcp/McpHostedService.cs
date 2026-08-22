using System.Net;
using System.Text.Json;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Agent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace BetterGenshinImpact.Service.Mcp;

/// <summary>
/// 在 WPF 主进程内托管仅监听回环地址的 MCP Streamable HTTP 服务器。
/// </summary>
public sealed class McpHostedService(
    IServiceProvider applicationServices,
    McpCommandCatalog commandCatalog,
    McpDetachedTaskRegistry detachedTaskRegistry,
    ILogger<McpHostedService> logger) : IHostedService
{
    private WebApplication? _webApplication;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var options = CommandLineOptions.Instance;
        if (!options.McpEnabled)
        {
            logger.LogDebug("MCP 服务未启用；使用 --mcp 启动。");
            return;
        }

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(McpHostedService).Assembly.GetName().Name,
            Args = [],
        });
        builder.WebHost.UseUrls($"http://127.0.0.1:{options.McpPort}");
        builder.Services.AddSingleton(new McpApplicationServices(applicationServices));
        builder.Services.AddSingleton(commandCatalog);
        builder.Services.AddSingleton(detachedTaskRegistry);
        builder.Services.AddSingleton(applicationServices.GetRequiredService<McpAgentService>());
        builder.Services
            .AddMcpServer(serverOptions =>
            {
                serverOptions.ServerInfo = new()
                {
                    Name = "better-genshin-impact",
                    Version = typeof(McpHostedService).Assembly.GetName().Version?.ToString() ?? "unknown",
                };
                serverOptions.ServerInstructions = """
                                                   这是 BetterGI（更好的原神）的本机自动化 MCP 服务。它能控制截图器和游戏输入、运行独立任务、调度配置组、JS/地图追踪/键鼠脚本、一条龙、兑换码，以及管理设置、脚本仓库和订阅。
                                                   首次使用或不理解业务对象时先调用 bgi_get_capabilities；独立任务先调用 bgi_list_game_tasks 和 bgi_get_game_task_settings；JS 调度先调用 bgi_list_javascript_scripts、bgi_get_javascript_script、bgi_get_script_group。
                                                   不要猜 groupName、folderName、projectIndex 或设置字段。先用读取工具获得真实值和 settingsSchema，再修改或执行。JS 设置属于配置组内的项目实例，不是脚本全局设置。
                                                   优先使用 bgi_* 专用工具；只有没有专用工具时才使用 bgi_list_commands + bgi_invoke_command。危险操作必须遵守 confirm、confirmHttpAccess、confirmShellCommand、confirmSensitive 等确认参数。
                                                   BetterGI 同时只允许一个独立任务。长任务需要真正停止时优先调用 bgi_stop_current_task_and_wait(confirm=true)，也可调用 bgi_interrupt_current_script；取消 MCP 请求本身不保证游戏任务停止。暂停是协作式的，不保证所有第三方 JS 立即响应。
                                                   """;
            })
            .WithHttpTransport(transportOptions => transportOptions.Stateless = true)
            .WithTools<McpSystemTools>()
            .WithTools<McpConfigurationTools>()
            .WithTools<McpRepositoryTools>()
            .WithTools<McpRepositorySearchTools>()
            .WithTools<McpSchedulerTools>()
            .WithTools<McpGameTaskTools>()
            .WithTools<McpGameLifecycleTools>()
            .WithTools<McpRuntimeControlTools>()
            .WithTools<McpDetachedTaskTools>()
            .WithTools<McpWorkflowTools>()
            .WithTools<McpCommandTools>();

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (!IsLoopbackRequest(context) || !IsAllowedHost(context) || !IsAllowedOrigin(context))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var origin = context.Request.Headers.Origin.ToString();
            if (!string.IsNullOrWhiteSpace(origin))
            {
                context.Response.Headers.AccessControlAllowOrigin = origin;
                context.Response.Headers.AccessControlAllowHeaders = "Content-Type";
                context.Response.Headers.AccessControlAllowMethods = "GET, POST, DELETE, OPTIONS";
                context.Response.Headers.Vary = "Origin";
            }

            if (HttpMethods.IsOptions(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            await next();
        });
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            server = "better-genshin-impact",
            endpoint = "/mcp",
        }));
        app.MapMcp("/mcp");
        app.MapGet("/agent/status", (McpAgentService agentService) => Results.Ok(agentService.GetStatus()));
        app.MapGet("/agent/models", async (McpAgentService agentService, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await agentService.GetModelsAsync(ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Problem(ex.GetBaseException().Message, statusCode: StatusCodes.Status502BadGateway);
            }
        });
        app.MapGet("/agent/conversation",
            (McpAgentService agentService) => Results.Ok(agentService.LoadConversation()));
        app.MapDelete("/agent/conversation", async (McpAgentService agentService, CancellationToken ct) =>
        {
            await agentService.ClearConversationAsync(ct);
            return Results.Ok(new { cleared = true });
        });
        app.MapPost("/agent/chat",
            async (AgentHttpChatRequest request, McpAgentService agentService, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.Message))
                    return Results.BadRequest(new { error = "message 不能为空。" });
                try
                {
                    if (request.ResetConversation) await agentService.ClearConversationAsync(ct);
                    var history = agentService.LoadConversation();
                    var result = await agentService.ChatAsync(history, request.Message, ct);
                    return Results.Ok(result);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return Results.Problem(ex.GetBaseException().Message, statusCode: StatusCodes.Status502BadGateway);
                }
            });
        app.MapPost("/agent/chat/stream", async (
            AgentHttpChatRequest request,
            McpAgentService agentService,
            HttpContext context,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "message 不能为空。" }, ct);
                return;
            }

            context.Response.ContentType = "text/event-stream; charset=utf-8";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Append("X-Accel-Buffering", "no");
            try
            {
                if (request.ResetConversation) await agentService.ClearConversationAsync(ct);
                var history = agentService.LoadConversation();
                _ = await agentService.ChatStreamingAsync(
                    history,
                    request.Message,
                    async (streamEvent, token) =>
                    {
                        await WriteSseEvent(context.Response, streamEvent.Type, streamEvent, token);
                    },
                    ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 客户端断开连接。
            }
            catch (Exception ex)
            {
                await WriteSseEvent(context.Response, "error", new
                {
                    type = "error",
                    message = ex.GetBaseException().Message,
                }, CancellationToken.None);
            }
        });

        _webApplication = app;
        await app.StartAsync(cancellationToken);
        logger.LogInformation("MCP 服务已启动：http://127.0.0.1:{Port}/mcp", options.McpPort);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_webApplication is null)
        {
            return;
        }

        await _webApplication.StopAsync(cancellationToken);
        await _webApplication.DisposeAsync();
        _webApplication = null;
    }

    private static bool IsLoopbackRequest(HttpContext context)
    {
        var remoteAddress = context.Connection.RemoteIpAddress;
        return remoteAddress is null || IPAddress.IsLoopback(remoteAddress);
    }

    private static bool IsAllowedHost(HttpContext context)
    {
        var host = context.Request.Host.Host;
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || host == "127.0.0.1"
               || host == "::1";
    }

    private static bool IsAllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin))
        {
            return true;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
               && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                   || IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address));
    }

    private static async Task WriteSseEvent(
        HttpResponse response,
        string eventName,
        object payload,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, ConfigService.JsonOptions).Replace("\r", "").Replace("\n", "\\n");
        await response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}

public sealed record McpApplicationServices(IServiceProvider Services);