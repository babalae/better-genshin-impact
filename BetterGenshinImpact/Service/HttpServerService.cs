using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Service.Interface;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service;

/// <summary>
/// HTTP 服务器服务
/// </summary>
public class HttpServerService : IHostedService, IDisposable
{
    private readonly ILogger<HttpServerService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private WebApplication? _webApp;
    private readonly HttpServerConfig _config;
    private CancellationTokenSource? _cancellationTokenSource;

    public HttpServerService(
        ILogger<HttpServerService> logger,
        IConfigService configService,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _config = configService.Get().HttpServerConfig;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_config.Enabled)
        {
            _logger.LogInformation("HTTP 服务器未启用");
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        await StartWebServer();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_webApp != null)
        {
            await _webApp.StopAsync(cancellationToken);
            await _webApp.DisposeAsync();
            _webApp = null;
        }

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _logger.LogInformation("HTTP 服务器已停止");
    }

    private async Task StartWebServer()
    {
        try
        {
            var builder = WebApplication.CreateBuilder();

            // 配置 Kestrel 服务器
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(_config!.Port);
            });

            // 添加服务
            builder.Services.AddCors();
            
            if (_config!.EnableWebSocket)
            {
                builder.Services.AddSignalR();
            }

            // if (_config.EnableSwagger)
            // {
            //     builder.Services.AddEndpointsApiExplorer();
            //     // builder.Services.AddSwaggerGen();
            // }

            // 添加现有服务的引用
            builder.Services.AddSingleton(_serviceProvider.GetService<IConfigService>()!);

            _webApp = builder.Build();

            // 配置中间件
            if (_config.EnableCors)
            {
                _webApp.UseCors(policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                });
            }

            // if (_config.EnableSwagger)
            // {
            //     // _webApp.UseSwagger();
            //     // _webApp.UseSwaggerUI();
            // }

            // 配置路由
            ConfigureRoutes(_webApp);

            if (_config.EnableWebSocket)
            {
                _webApp.MapHub<BgiHub>("/bgi-hub");
            }

            // 启动服务器
            await _webApp.StartAsync(_cancellationTokenSource!.Token);
            
            var url = $"http://{_config.Host}:{_config.Port}";
            _logger.LogInformation("HTTP 服务器已启动: {Url}", url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动 HTTP 服务器失败");
            throw;
        }
    }

    private void ConfigureRoutes(WebApplication app)
    {
        // 健康检查
        app.MapGet("/health", () => new { status = "healthy", timestamp = DateTime.UtcNow })
           .WithTags("System")
           .WithSummary("健康检查");

        // 获取应用状态
        app.MapGet("/api/status", () =>
        {
        })
        .WithTags("Application")
        .WithSummary("获取应用状态");

        // 获取配置
        app.MapGet("/api/config", (IConfigService configService) =>
        {
            var config = configService.Get();
            return Results.Json(config, ConfigService.JsonOptions);
        })
        .WithTags("Configuration")
        .WithSummary("获取应用配置");

        // 更新配置（仅部分配置）
        app.MapPost("/api/config", async (HttpContext context, IConfigService configService) =>
        {
            try
            {
                var jsonDoc = await JsonDocument.ParseAsync(context.Request.Body);
                var config = configService.Get();
                configService.Save();
                return Results.Ok(new { message = "配置更新成功" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithTags("Configuration")
        .WithSummary("更新应用配置");

        // 启动/停止任务
        app.MapPost("/api/tasks/{action}", (string action) =>
        {
            
        })
        .WithTags("Tasks")
        .WithSummary("启动或停止任务");

        // 获取任务列表（脚本）
        app.MapGet("/api/scripts", () =>
        {
            // 这里可以返回可用的脚本列表
            // 具体实现根据你的脚本管理逻辑
            return Results.Ok(new { scripts = new List<object>() });
        })
        .WithTags("Scripts")
        .WithSummary("获取可用脚本列表");

        // // 静态文件服务（可选）
        // app.MapGet("/", () => Results.Redirect("/swagger"));
    }

    public void Dispose()
    {
        _webApp?.DisposeAsync().AsTask().Wait();
        _cancellationTokenSource?.Dispose();
    }
}

/// <summary>
/// SignalR Hub，用于 WebSocket 通信
/// </summary>
public class BgiHub : Hub
{
    private readonly ILogger<BgiHub> _logger;

    public BgiHub(ILogger<BgiHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("WebSocket 客户端已连接: {ConnectionId}", Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, "BgiClients");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("WebSocket 客户端已断开: {ConnectionId}", Context.ConnectionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "BgiClients");
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// 向所有连接的客户端发送消息
    /// </summary>
    public async Task BroadcastMessage(string message, object? data = null)
    {
        await Clients.Group("BgiClients").SendAsync("ReceiveMessage", new
        {
            message,
            data,
            timestamp = DateTime.UtcNow
        });
    }
}