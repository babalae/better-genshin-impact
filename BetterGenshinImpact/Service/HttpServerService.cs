using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.LogParse;
using System.Text.Json;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Service.Interface;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            // 初始化LogAnalyzer
            LogAnalyzer.Initialize();
            
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

            // 配置静态文件服务
            var staticPath = Path.Combine(Directory.GetCurrentDirectory(), "GameTask", "LogParse", "static");
            _logger.LogInformation($"日志分析静态文件路径: {staticPath}");
            if (Directory.Exists(staticPath))
            {
                _webApp.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(staticPath),
                    RequestPath = "/loganalyzer"
                });
                _logger.LogInformation($"已配置日志分析器静态文件服务：{staticPath}");
            }
            else
            {
                _logger.LogWarning($"日志分析器静态文件目录不存在：{staticPath}");
            }

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

        // 日志分析器静态文件路由
        ConfigureLogAnalyzerRoutes(app);

        // // 静态文件服务（可选）
        // app.MapGet("/", () => Results.Redirect("/swagger"));
    }

    private void ConfigureLogAnalyzerRoutes(WebApplication app)
    {
        var staticPath = Path.Combine(Directory.GetCurrentDirectory(), "GameTask", "LogParse", "static");

        // 根路径返回index.html
        app.MapGet("/CanLiang", async context =>
        {
            var indexPath = Path.Combine(staticPath, "index.html");
            if (File.Exists(indexPath))
            {
                context.Response.ContentType = "text/html";
                await context.Response.SendFileAsync(indexPath);
            }
            else
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("index.html not found");
            }
        })
        .WithTags("LogAnalyzer")
        .WithSummary("参量质变仪首页");
        
        // 处理静态文件请求 /CanLiang/{filename}
        app.MapGet("/CanLiang/{*filename}", async context =>
        {
            var filename = context.Request.RouteValues["filename"]?.ToString();
            if (string.IsNullOrEmpty(filename))
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("File not found");
                return;
            }
            
            // 构建文件路径，支持_next目录下的文件
            var filePath = Path.Combine(staticPath, filename);
            
            if (File.Exists(filePath))
            {
                // 获取文件的MIME类型
                var contentType = GetContentType(filePath);
                context.Response.ContentType = contentType;
                await context.Response.SendFileAsync(filePath);
            }
            else
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync($"File not found: {filename}");
            }
        })
        .WithTags("LogAnalyzer")
        .WithSummary("参量质变仪静态文件");

        // API路由
        app.MapGet("/api/LogList", async context =>
        {
            var response = LogAnalyzer.GetLogListData();
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        })
        .WithTags("LogAnalyzer")
        .WithSummary("获取日志列表");

        app.MapGet("/api/analyse", async context =>
        {
            var date = context.Request.Query["date"].FirstOrDefault() ?? "all";
            var response = LogAnalyzer.GetAnalyseData(date);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        })
        .WithTags("LogAnalyzer")
        .WithSummary("分析日志数据");

        app.MapGet("/api/item-trend", async context =>
        {
            var itemName = context.Request.Query["item"].FirstOrDefault() ?? "";
            var response = LogAnalyzer.GetItemTrendData(itemName);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        })
        .WithTags("LogAnalyzer")
        .WithSummary("获取物品趋势");

        app.MapGet("/api/duration-trend", async context =>
        {
            var response = LogAnalyzer.GetDurationTrendData();
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        })
        .WithTags("LogAnalyzer")
        .WithSummary("获取时长趋势");

        app.MapGet("/api/total-items-trend", async context =>
        {
            var response = LogAnalyzer.GetTotalItemsTrendData();
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        })
        .WithTags("LogAnalyzer")
        .WithSummary("获取总物品趋势");
    }

    /// <summary>
    /// 根据文件扩展名获取MIME类型
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>MIME类型</returns>
    private string GetContentType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".eot" => "application/vnd.ms-fontobject",
            ".otf" => "font/otf",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
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