using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using SixLabors.ImageSharp;

namespace BetterGenshinImpact.Service.Notifier;

/// <summary>按设置监听 IPv4 网卡的 Yunzai WebSocket 控制端点。</summary>
public sealed class ExternalControlService : IHostedService, IDisposable
{
    private const int MaxMessageBytes = 4096;
    private static readonly JsonSerializerOptions WebSocketJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly WorkerController _workerController;
    private readonly WorkerTaskExecutor _workerTaskExecutor;
    private readonly GameRuntimeService _runtimeService;
    private readonly ILogger<ExternalControlService> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _sync = new();
    private NotificationConfig? _config;
    private HttpListener? _listener;
    private Task? _listenTask;
    private Task? _reverseConnectTask;
    private CancellationTokenSource? _reverseConnectionLifetime;
    private WebSocket? _reverseSocket;
    private readonly SemaphoreSlim _reverseSendLock = new(1, 1);

    public bool IsReverseConnected => _reverseSocket?.State == WebSocketState.Open;

    public ExternalControlService(WorkerController workerController, WorkerTaskExecutor workerTaskExecutor,
        GameRuntimeService runtimeService, ILogger<ExternalControlService> logger)
    {
        _workerController = workerController;
        _workerTaskExecutor = workerTaskExecutor;
        _runtimeService = runtimeService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _config = TaskContext.Instance().Config.NotificationConfig;
        if (InstanceBootstrap.Current.Context.IsHeadless && !_config.ExternalControlReverse) return Task.CompletedTask;
        _config.PropertyChanged += OnConfigChanged;
        Reconcile();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _lifetime.Cancel();
        _reverseConnectionLifetime?.Cancel();
        StopListener();
        if (_reverseConnectTask is not null)
        {
            try { await _reverseConnectTask.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { }
        }
        if (_listenTask is not null)
        {
            try { await _listenTask.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { }
            catch (HttpListenerException) { }
        }
    }

    public void Dispose()
    {
        if (_config is not null) _config.PropertyChanged -= OnConfigChanged;
        _lifetime.Cancel();
        _reverseConnectionLifetime?.Cancel();
        StopListener();
        _lifetime.Dispose();
        _reverseSendLock.Dispose();
    }

    public async Task SendNotificationAsync(BaseNotificationData data, string userOpenId, string groupOpenId,
        CancellationToken cancellationToken = default)
    {
        var socket = _reverseSocket;
        if (socket?.State != WebSocketState.Open)
            throw new InvalidOperationException("Yunzai 反向 WebSocket 尚未连接。");

        string? screenshot = null;
        if (data.Screenshot is not null)
        {
            using var imageStream = new MemoryStream();
            data.Screenshot.SaveAsJpeg(imageStream);
            screenshot = Convert.ToBase64String(imageStream.ToArray());
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "notification",
            eventName = data.Event,
            result = data.Result.ToString(),
            timestamp = data.Timestamp,
            message = data.Message ?? string.Empty,
            screenshotBase64 = screenshot,
            userOpenId,
            groupOpenId
        });
        await _reverseSendLock.WaitAsync(cancellationToken);
        try
        {
            if (socket.State != WebSocketState.Open)
                throw new InvalidOperationException("Yunzai 反向 WebSocket 连接已断开。");
            await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally { _reverseSendLock.Release(); }
    }

    public async Task SendCommandReplyAsync(string userOpenId, string groupOpenId, string message,
        CancellationToken cancellationToken = default)
    {
        var socket = _reverseSocket;
        if (socket?.State != WebSocketState.Open)
            throw new InvalidOperationException("Yunzai 反向 WebSocket 尚未连接。");
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "commandReply",
            eventName = "QQ 远程命令",
            result = "Success",
            timestamp = DateTime.Now,
            message,
            userOpenId,
            groupOpenId
        });
        await _reverseSendLock.WaitAsync(cancellationToken);
        try
        {
            if (socket.State != WebSocketState.Open)
                throw new InvalidOperationException("Yunzai 反向 WebSocket 连接已断开。");
            await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally { _reverseSendLock.Release(); }
    }

    private void OnConfigChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NotificationConfig.ExternalControlEnabled)
            or nameof(NotificationConfig.ExternalControlPort)
            or nameof(NotificationConfig.ExternalControlIp)
            or nameof(NotificationConfig.ExternalControlReverse)
            or nameof(NotificationConfig.ExternalControlRemoteIp)
            or nameof(NotificationConfig.ExternalControlToken)) Reconcile();
    }

    private void Reconcile()
    {
        lock (_sync)
        {
            StopListener();
            _listenTask = null;
            _reverseConnectionLifetime?.Cancel();
            _reverseConnectionLifetime?.Dispose();
            _reverseConnectionLifetime = null;
            _reverseConnectTask = null;
            if (_config is null || !_config.ExternalControlEnabled || _lifetime.IsCancellationRequested) return;
            if (_config.ExternalControlPort is < 1024 or > 65535 || string.IsNullOrWhiteSpace(_config.ExternalControlToken))
            {
                _logger.LogWarning("外部控制未启动：端口或鉴权令牌无效");
                return;
            }

            if (_config.ExternalControlReverse)
            {
                if (!IPAddress.TryParse(_config.ExternalControlRemoteIp, out var remoteAddress)
                    || remoteAddress.AddressFamily != AddressFamily.InterNetwork)
                {
                    _logger.LogWarning("反向 WebSocket 未启动：Yunzai 服务端 IP 无效");
                    return;
                }
                _reverseConnectionLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                _reverseConnectTask = ConnectToYunzaiAsync(remoteAddress, _config.ExternalControlPort,
                    _config.ExternalControlToken, _reverseConnectionLifetime.Token);
                _logger.LogInformation("外部控制将反向连接 Yunzai WebSocket {Address}:{Port}", remoteAddress, _config.ExternalControlPort);
                return;
            }

            if (!IPAddress.TryParse(_config.ExternalControlIp, out var bindAddress)
                || bindAddress.AddressFamily != AddressFamily.InterNetwork)
            {
                _logger.LogWarning("外部控制未启动：监听 IP 无效");
                return;
            }

            var listener = new HttpListener();
            var prefixHost = bindAddress.Equals(IPAddress.Any) ? "+" : bindAddress.ToString();
            listener.Prefixes.Add($"http://{prefixHost}:{_config.ExternalControlPort}/");
            try
            {
                listener.Start();
                _listener = listener;
                _listenTask = ListenAsync(listener, _config.ExternalControlToken, _lifetime.Token);
                _logger.LogInformation("Yunzai 外部控制已监听 {Address}:{Port}", bindAddress, _config.ExternalControlPort);
            }
            catch (System.Exception ex)
            {
                listener.Close();
                _logger.LogError(ex, "启动 Yunzai 外部控制 WebSocket 失败");
            }
        }
    }

    private async Task ConnectToYunzaiAsync(IPAddress address, int port, string token, CancellationToken cancellationToken)
    {
        var role = InstanceBootstrap.Current.Context.IsHeadless ? "worker" : "controller";
        var endpoint = new Uri($"ws://{address}:{port}/?role={role}");
        while (!cancellationToken.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
            try
            {
                await socket.ConnectAsync(endpoint, cancellationToken);
                _reverseSocket = socket;
                _logger.LogInformation("已连接 Yunzai WebSocket 服务端 {Address}:{Port}，连接角色 {Role}", address, port, role);
                var buffer = new byte[1024];
                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    using var stream = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(buffer, cancellationToken);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        if (result.MessageType != WebSocketMessageType.Text || stream.Length + result.Count > MaxMessageBytes)
                            throw new InvalidDataException("WebSocket 命令消息无效或过大。");
                        stream.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    var request = JsonSerializer.Deserialize<ControlRequest>(Encoding.UTF8.GetString(stream.ToArray()), WebSocketJsonOptions);
                    _logger.LogInformation("收到 Yunzai 反向 WebSocket 控制命令：{Command}", request?.Message ?? "<空消息>");
                    var response = await ExecuteAsync(request);
                    var payload = JsonSerializer.SerializeToUtf8Bytes(response, WebSocketJsonOptions);
                    await _reverseSendLock.WaitAsync(cancellationToken);
                    try { await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken); }
                    finally { _reverseSendLock.Release(); }
                    _logger.LogInformation("已向 Yunzai 返回控制命令结果：{Message}", response.Message);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (System.Exception ex) { _logger.LogWarning(ex, "连接或处理 Yunzai 反向 WebSocket 失败"); }
            finally { if (ReferenceEquals(_reverseSocket, socket)) _reverseSocket = null; }

            try { await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void StopListener()
    {
        try { _listener?.Stop(); } catch (ObjectDisposedException) { }
        _listener?.Close();
        _listener = null;
    }

    private async Task ListenAsync(HttpListener listener, string token, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync().WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested || !listener.IsListening) { break; }
            catch (ObjectDisposedException) { break; }

            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.Close();
                continue;
            }
            var supplied = context.Request.Headers["Authorization"]?.Replace("Bearer ", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (!FixedTimeEquals(token, supplied))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                context.Response.Close();
                continue;
            }

            _ = HandleClientAsync(context, cancellationToken);
        }
    }

    private async Task HandleClientAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        WebSocket? socket = null;
        try
        {
            socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
            var buffer = new byte[1024];
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    if (result.MessageType != WebSocketMessageType.Text || stream.Length + result.Count > MaxMessageBytes)
                        throw new InvalidDataException("WebSocket 命令消息无效或过大。");
                    stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var request = JsonSerializer.Deserialize<ControlRequest>(Encoding.UTF8.GetString(stream.ToArray()), WebSocketJsonOptions);
                var response = await ExecuteAsync(request);
                var payload = JsonSerializer.SerializeToUtf8Bytes(response, WebSocketJsonOptions);
                await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (System.Exception ex) { _logger.LogWarning(ex, "处理 Yunzai 外部控制连接失败"); }
        finally
        {
            if (socket is not null)
            {
                try { if (socket.State == WebSocketState.Open) await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None); }
                catch (WebSocketException) { }
                socket.Dispose();
            }
        }
    }

    private async Task<ControlResponse> ExecuteAsync(ControlRequest? request)
    {
        var message = request?.Message?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(request?.UserOpenId) || !IsAuthorized(request.UserOpenId))
            return new ControlResponse { Success = false, Message = "未授权的 QQ OpenID。" };
        if (!QqCommandService.TryParseExternalCommand(message, out var command, out var argument))
            return new ControlResponse { Success = false, Message = "不支持的命令。" };

        var text = await QqCommandService.ExecuteExternalCommandAsync(command, argument, _workerController, _workerTaskExecutor, _runtimeService);
        return await BuildResponseAsync(text);
    }

    private bool IsAuthorized(string userOpenId)
    {
        var allowList = _config?.QqCommandUserOpenIds.Split([',', ';', '，', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        if (allowList.Length == 0 && !string.IsNullOrWhiteSpace(_config?.QqOpenId)) allowList = [_config.QqOpenId.Trim()];
        return allowList.Contains(userOpenId, StringComparer.Ordinal);
    }

    private async Task<ControlResponse> BuildResponseAsync(string message)
    {
        string? screenshot = null;
        try
        {
            if (_workerController.IsConnected)
            {
                var imageBytes = await _workerController.GetScreenshotAsync();
                if (imageBytes is not null) screenshot = Convert.ToBase64String(imageBytes);
            }
            else if (_runtimeService.IsRunning && TaskContext.Instance().Runtime?.Capture is { } capture)
            {
                using var frame = capture.Capture();
                if (frame?.Frame is { } mat)
                {
                    using var resized = new Mat();
                    var targetWidth = Math.Min(mat.Width, 900);
                    Cv2.Resize(mat, resized, new OpenCvSharp.Size(targetWidth, Math.Max(1, targetWidth * mat.Height / mat.Width)));
                    Cv2.ImEncode(".jpg", resized, out var imageBytes, [new ImageEncodingParam(ImwriteFlags.JpegQuality, 78)]);
                    screenshot = Convert.ToBase64String(imageBytes);
                }
            }
        }
        catch (System.Exception ex) { _logger.LogDebug(ex, "生成外部控制截图失败"); }

        var logs = ReadLogTail(Path.Combine(AppContext.BaseDirectory, "log"), 14);
        var responseLogs = (logs.Length == 0 ? [] : logs).Append(message).TakeLast(15).ToArray();
        return new ControlResponse
        {
            Success = true,
            Message = message,
            Logs = responseLogs,
            ScreenshotBase64 = screenshot,
            BetterGiVersion = Global.Version
        };
    }

    private static string[] ReadLogTail(string directory, int lineCount)
    {
        try
        {
            var lines = new System.Collections.Generic.Queue<string>(lineCount);
            if (!Directory.Exists(directory)) return [];
            var path = Directory.GetFiles(directory, "better-genshin-impact*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (path is null) return [];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            const long maxTailBytes = 256 * 1024;
            if (stream.Length > maxTailBytes)
            {
                stream.Position = stream.Length - maxTailBytes;
                int read;
                do { read = stream.ReadByte(); }
                while (read >= 0 && read != '\n');
            }
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (reader.ReadLine() is { } line)
            {
                if (lines.Count == lineCount) lines.Dequeue();
                lines.Enqueue(line);
            }
            return lines.ToArray();
        }
        catch (IOException) { return []; }
    }

    private static bool FixedTimeEquals(string expected, string? actual)
    {
        if (string.IsNullOrEmpty(actual)) return false;
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private sealed class ControlRequest
    {
        public string? Message { get; init; }
        public string? UserOpenId { get; init; }
    }

    private sealed class ControlResponse
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public string[] Logs { get; init; } = [];
        public string? ScreenshotBase64 { get; init; }
        public string BetterGiVersion { get; init; } = string.Empty;
    }
}
