using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.ExternalAccess.Logging;
using BetterGenshinImpact.Service.ExternalAccess.Mcp;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Interface;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using Serilog;
using System;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service.ExternalAccess;

/// <summary>
/// 在 BetterGI 主实例内托管仅监听 loopback 的 HTTP、WebSocket 与 MCP 服务。
/// </summary>
public sealed class ExternalAccessHost : IHostedService
{
    /// <summary>
    /// 全局配置读写服务。
    /// </summary>
    private readonly IConfigService _configService;

    /// <summary>
    /// BetterGI 多实例上下文服务。
    /// </summary>
    private readonly InstanceService _instanceService;

    /// <summary>
    /// 设置页面可观察的运行状态。
    /// </summary>
    private readonly ExternalAccessState _state;

    /// <summary>
    /// 访问令牌生成与校验服务。
    /// </summary>
    private readonly ExternalAccessTokenService _tokenService;

    /// <summary>
    /// 与传输协议无关的 BetterGI 能力服务。
    /// </summary>
    private readonly IBgiExternalCapabilityService _capabilityService;

    /// <summary>
    /// 实时日志分发器。
    /// </summary>
    private readonly ExternalLogHub _logHub;

    /// <summary>
    /// WebSocket 日志连接处理器。
    /// </summary>
    private readonly ExternalLogWebSocketHandler _logWebSocketHandler;

    /// <summary>
    /// 外部访问服务自身的日志记录器。
    /// </summary>
    private readonly ILogger<ExternalAccessHost> _logger;

    /// <summary>
    /// 实际承载外部协议端点的子 WebApplication。
    /// </summary>
    private WebApplication? _webApplication;

    /// <summary>
    /// 创建 BetterGI 外部访问宿主。
    /// </summary>
    public ExternalAccessHost(
        IConfigService configService,
        InstanceService instanceService,
        ExternalAccessState state,
        ExternalAccessTokenService tokenService,
        IBgiExternalCapabilityService capabilityService,
        ExternalLogHub logHub,
        ExternalLogWebSocketHandler logWebSocketHandler,
        ILogger<ExternalAccessHost> logger)
    {
        _configService = configService;
        _instanceService = instanceService;
        _state = state;
        _tokenService = tokenService;
        _capabilityService = capabilityService;
        _logHub = logHub;
        _logWebSocketHandler = logWebSocketHandler;
        _logger = logger;
    }

    /// <summary>
    /// 按启动时配置创建并启动 loopback 子 WebApplication。
    /// </summary>
    /// <param name="cancellationToken">应用启动取消令牌。</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_instanceService.Context.IsRoot)
        {
            _state.Update(ExternalAccessServiceStatus.Disabled, "仅 Primary 实例可以启动外部访问服务", "未启动");
            return;
        }

        var config = _configService.Get().ExternalAccessConfig;
        var httpApiEnabled = config.HttpApiEnabled;
        var webSocketEnabled = config.WebSocketEnabled;
        var mcpEnabled = config.McpEnabled;
        var port = config.Port;

        if (!httpApiEnabled && !webSocketEnabled && !mcpEnabled)
        {
            _state.Update(ExternalAccessServiceStatus.Disabled, "外部访问服务未启用", "未启动");
            return;
        }

        if (port is < 1024 or > 65535)
        {
            _state.Update(ExternalAccessServiceStatus.Failed, "端口必须在 1024 到 65535 之间", "未启动");
            _logger.LogError("外部访问服务端口 {Port} 不合法", port);
            return;
        }

        var address = $"http://127.0.0.1:{port}";
        _state.Update(ExternalAccessServiceStatus.Starting, "正在启动本机外部访问服务", address);

        try
        {
            // 令牌生成和持久化也属于服务启动流程，失败时只更新状态而不终止 WPF 主程序。
            if (httpApiEnabled || mcpEnabled)
            {
                _tokenService.EnsureToken();
            }

            _webApplication = BuildApplication(port, httpApiEnabled, webSocketEnabled, mcpEnabled);
            await _webApplication.StartAsync(cancellationToken).ConfigureAwait(false);
            _state.Update(ExternalAccessServiceStatus.Running, "配置开关和端口将在下次重启时重新加载", address);
            _logger.LogInformation(
                "外部访问服务已启动：{Address}，HTTP={HttpEnabled}，WebSocket={WebSocketEnabled}，MCP={McpEnabled}",
                address,
                httpApiEnabled,
                webSocketEnabled,
                mcpEnabled);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _state.Update(ExternalAccessServiceStatus.Failed, exception.Message, address);
            _logger.LogError(exception, "外部访问服务启动失败：{Address}", address);
            try
            {
                await DisposeWebApplicationAsync().ConfigureAwait(false);
            }
            catch (Exception disposeException)
            {
                // 失败宿主的清理异常也不能中断 WPF 主程序启动。
                _logger.LogWarning(disposeException, "释放启动失败的外部访问服务时发生异常");
            }
        }
    }

    /// <summary>
    /// 停止并释放外部访问子 WebApplication。
    /// </summary>
    /// <param name="cancellationToken">应用停止取消令牌。</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_webApplication is not null)
        {
            try
            {
                await _webApplication.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 主程序正在强制结束，继续释放子宿主。
            }
            finally
            {
                await DisposeWebApplicationAsync().ConfigureAwait(false);
            }
        }

        if (_state.Status != ExternalAccessServiceStatus.Disabled)
        {
            _state.Update(ExternalAccessServiceStatus.Stopped, "外部访问服务已停止", "未启动");
        }
    }

    /// <summary>
    /// 创建并配置承载外部协议的子 WebApplication。
    /// </summary>
    /// <param name="port">loopback 监听端口。</param>
    /// <param name="httpApiEnabled">是否映射 HTTP API。</param>
    /// <param name="webSocketEnabled">是否映射 WebSocket 日志。</param>
    /// <param name="mcpEnabled">是否映射 MCP。</param>
    /// <returns>尚未启动的 WebApplication。</returns>
    private WebApplication BuildApplication(
        int port,
        bool httpApiEnabled,
        bool webSocketEnabled,
        bool mcpEnabled)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(ExternalAccessHost).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Production
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: false);
        builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(port));

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });
        builder.Services.AddCors(options => options.AddPolicy(
            "ExternalAccess",
            policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
        builder.Services.AddSingleton(_capabilityService);
        builder.Services.AddSingleton(_tokenService);
        builder.Services.AddSingleton(_logHub);
        builder.Services.AddSingleton(_logWebSocketHandler);

        if (mcpEnabled)
        {
            builder.Services.AddMcpServer()
                .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
                .WithTools<BgiMcpTools>();
        }

        var app = builder.Build();
        ConfigurePipeline(app, httpApiEnabled, webSocketEnabled, mcpEnabled);
        return app;
    }

    /// <summary>
    /// 配置 Host 校验、CORS、Bearer 鉴权及已启用的协议端点。
    /// </summary>
    /// <param name="app">外部访问子 WebApplication。</param>
    /// <param name="httpApiEnabled">是否映射 HTTP API。</param>
    /// <param name="webSocketEnabled">是否映射 WebSocket 日志。</param>
    /// <param name="mcpEnabled">是否映射 MCP。</param>
    private void ConfigurePipeline(
        WebApplication app,
        bool httpApiEnabled,
        bool webSocketEnabled,
        bool mcpEnabled)
    {
        app.Use(async (context, next) =>
        {
            if (!IsLoopbackHost(context.Request.Host.Host))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Host 必须是 loopback 地址。", context.RequestAborted);
                return;
            }

            await next(context);
        });

        app.UseRouting();
        // CORS 策略通过端点元数据按需应用，未启用的协议路径仍由框架返回 404。
        app.UseCors();
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsOptions(context.Request.Method)
                || !RequiresBearerToken(context.Request.Path, httpApiEnabled, mcpEnabled))
            {
                await next(context);
                return;
            }

            if (!_tokenService.IsAuthorized(context.Request.Headers.Authorization))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers["WWW-Authenticate"] = "Bearer";
                return;
            }

            await next(context);
        });

        if (httpApiEnabled)
        {
            app.MapGet("/api/v1/health", async (CancellationToken cancellationToken) =>
                    Results.Ok(await _capabilityService.GetHealthAsync(cancellationToken).ConfigureAwait(false)))
                .RequireCors("ExternalAccess");
            app.MapGet("/api/v1/version", async (CancellationToken cancellationToken) =>
                    Results.Ok(await _capabilityService.GetVersionAsync(cancellationToken).ConfigureAwait(false)))
                .RequireCors("ExternalAccess");
        }

        if (webSocketEnabled)
        {
            // AllowedOrigins 为空表示按产品约定允许所有 Origin；该端点也明确不使用 Bearer 鉴权。
            app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
            app.Map("/ws/v1/logs", _logWebSocketHandler.HandleAsync);
        }

        if (mcpEnabled)
        {
            app.MapMcp("/mcp").RequireCors("ExternalAccess");
        }
    }

    /// <summary>
    /// 判断请求 Host 是否为 localhost 或 loopback IP，阻止 DNS rebinding Host。
    /// </summary>
    /// <param name="host">HTTP Host 中不含端口的主机部分。</param>
    /// <returns>是否为本机回环地址。</returns>
    private static bool IsLoopbackHost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }

    /// <summary>
    /// 判断当前路径是否属于已启用且需要 Bearer 令牌的 HTTP API 或 MCP。
    /// </summary>
    /// <param name="path">请求路径。</param>
    /// <param name="httpApiEnabled">HTTP API 是否已映射。</param>
    /// <param name="mcpEnabled">MCP 是否已映射。</param>
    /// <returns>是否需要鉴权。</returns>
    private static bool RequiresBearerToken(PathString path, bool httpApiEnabled, bool mcpEnabled)
    {
        return httpApiEnabled && path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
               || mcpEnabled && path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 释放当前子 WebApplication 并清空引用。
    /// </summary>
    private async ValueTask DisposeWebApplicationAsync()
    {
        var app = _webApplication;
        _webApplication = null;
        if (app is not null)
        {
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }
}
