using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Service.Interface;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace BetterGenshinImpact.Service;

public class HttpLogServerService : ILogEventSink, IDisposable
{
    private readonly IConfigService _configService;
    private HttpListener _listener;
    private CancellationTokenSource _cts;
    private readonly ConcurrentQueue<string> _logBuffer = new();
    private const int MaxLogLines = 1000;
    private readonly MessageTemplateTextFormatter _formatter;
    private readonly ConcurrentDictionary<HttpListenerResponse, bool> _clients = new();

    public HttpLogServerService(IConfigService configService)
    {
        _configService = configService;
        _formatter = new MessageTemplateTextFormatter("[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

        _configService.Get().OtherConfig.HttpLogServerConfig.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(OtherConfig.HttpLogServer.Enabled) ||
                e.PropertyName == nameof(OtherConfig.HttpLogServer.Port) ||
                e.PropertyName == nameof(OtherConfig.HttpLogServer.ListenAddress))
            {
                RestartServer();
            }
        };

        StartServer();
    }

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < LogEventLevel.Information) return;

        using var writer = new StringWriter();
        _formatter.Format(logEvent, writer);
        var logMessage = writer.ToString();

        var levelStr = logEvent.Level.ToString();
        var color = levelStr switch
        {
            "Warning" => "#ffcc00",
            "Error" => "#ff3333",
            "Fatal" => "#ff0000",
            _ => "#d4d4d4"
        };

        var logData = new { message = logMessage, color = color };
        var json = System.Text.Json.JsonSerializer.Serialize(logData);

        _logBuffer.Enqueue(json);
        while (_logBuffer.Count > MaxLogLines)
        {
            _logBuffer.TryDequeue(out _);
        }

        BroadcastLog(json);
    }

    private void BroadcastLog(string json)
    {
        var data = $"data: {Uri.EscapeDataString(json)}\n\n";
        var buffer = Encoding.UTF8.GetBytes(data);

        foreach (var client in _clients.Keys)
        {
            try
            {
                client.OutputStream.Write(buffer, 0, buffer.Length);
                client.OutputStream.Flush();
            }
            catch
            {
                _clients.TryRemove(client, out _);
            }
        }
    }

    private void StartServer()
    {
        var config = _configService.Get().OtherConfig.HttpLogServerConfig;
        if (!config.Enabled) return;

        _cts = new CancellationTokenSource();
        _listener = new HttpListener();

        var address = string.IsNullOrWhiteSpace(config.ListenAddress) ? "0.0.0.0" : config.ListenAddress;
        var prefix = address == "0.0.0.0" ? "+" : address;

        if (address != "0.0.0.0" && address != "localhost" && address != "127.0.0.1" && !System.Net.IPAddress.TryParse(address, out _))
        {
            Log.Error($"Invalid listen address: {address}. Using 0.0.0.0 instead.");
            prefix = "+";
            address = "0.0.0.0";
        }

        _listener.Prefixes.Add($"http://{prefix}:{config.Port}/");

        try
        {
            _listener.Start();
            Task.Run(() => ListenAsync(_cts.Token));
            Log.Information($"日志服务器已启动 {address}:{config.Port}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "启动日志服务器失败。请检查端口是否被占用，或是否需要 0.0.0.0 的管理员权限。");
        }
    }

    private void StopServer()
    {
        _cts?.Cancel();
        if (_listener != null && _listener.IsListening)
        {
            _listener.Stop();
            _listener.Close();
        }

        foreach (var client in _clients.Keys)
        {
            try { client.Close(); } catch { }
        }
        _clients.Clear();
    }

    private void RestartServer()
    {
        StopServer();
        StartServer();
    }

    private async Task ListenAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context), token);
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error accepting HTTP request");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            if (request.Url.AbsolutePath == "/")
            {
                ServeHtml(response);
            }
            else if (request.Url.AbsolutePath == "/logs")
            {
                await ServeLogsSseAsync(response);
            }
            else if (request.Url.AbsolutePath == "/screenshot")
            {
                ServeScreenshot(response);
            }
            else
            {
                response.StatusCode = 404;
                response.Close();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error handling HTTP request");
            try { response.StatusCode = 500; response.Close(); } catch { }
        }
    }

    private void ServeHtml(HttpListenerResponse response)
    {
        var html = @"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>BetterGI Log Server</title>
    <style>
        body { font-family: Consolas, monospace; background: #1e1e1e; color: #d4d4d4; margin: 0; padding: 10px; display: flex; flex-direction: column; height: 100vh; box-sizing: border-box; }
        #controls { margin-bottom: 10px; }
        button { padding: 5px 15px; background: #0e639c; color: white; border: none; cursor: pointer; }
        button:hover { background: #1177bb; }
        #log-container { flex: 1; overflow-y: auto; background: #252526; padding: 10px; border: 1px solid #333; white-space: pre-wrap; word-wrap: break-word; }
        #screenshot-container { margin-top: 10px; text-align: center; }
        #screenshot { max-width: 100%; max-height: 400px; display: none; }
    </style>
</head>
<body>
    <div id='controls'>
        <button onclick='getScreenshot()'>获取游戏截图</button>
        <label><input type='checkbox' id='auto-scroll' checked> 自动滚动到底部</label>
    </div>
    <div id='log-container'></div>
    <div id='screenshot-container'>
        <img id='screenshot' alt='Game Screenshot' />
    </div>

    <script>
        const logContainer = document.getElementById('log-container');
        const autoScroll = document.getElementById('auto-scroll');
        const screenshot = document.getElementById('screenshot');

        const evtSource = new EventSource('/logs');
        evtSource.onmessage = function(e) {
            const jsonStr = decodeURIComponent(e.data);
            try {
                const logData = JSON.parse(jsonStr);
                const span = document.createElement('span');
                span.style.color = logData.color;
                span.textContent = logData.message;
                logContainer.appendChild(span);
            } catch (err) {
                logContainer.appendChild(document.createTextNode(jsonStr));
            }
            if (autoScroll.checked) {
                logContainer.scrollTop = logContainer.scrollHeight;
            }
        };

        function getScreenshot() {
            screenshot.src = '/screenshot?t=' + new Date().getTime();
            screenshot.style.display = 'inline-block';
        }
    </script>
</body>
</html>";
        var buffer = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.Close();
    }

    private async Task ServeLogsSseAsync(HttpListenerResponse response)
    {
        response.ContentType = "text/event-stream";
        response.Headers.Add("Cache-Control", "no-cache");
        response.Headers.Add("Connection", "keep-alive");

        _clients.TryAdd(response, true);

        foreach (var log in _logBuffer)
        {
            var data = $"data: {Uri.EscapeDataString(log)}\n\n";
            var buffer = Encoding.UTF8.GetBytes(data);
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }
        await response.OutputStream.FlushAsync();
    }

    private void ServeScreenshot(HttpListenerResponse response)
    {
        try
        {
            using var mat = TaskTriggerDispatcher.GlobalGameCapture?.Capture();
            if (mat != null && !mat.Empty())
            {
                var bytes = mat.ImEncode(".jpg");
                response.ContentType = "image/jpeg";
                response.ContentLength64 = bytes.Length;
                response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            else
            {
                response.StatusCode = 404;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error capturing screenshot for HTTP server");
            response.StatusCode = 500;
        }
        finally
        {
            response.Close();
        }
    }

    public void Dispose()
    {
        StopServer();
    }
}
