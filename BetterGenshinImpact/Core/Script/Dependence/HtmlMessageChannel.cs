using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Core.Script.Dependence;

/// <summary>
/// WebView 与脚本之间的窗口级消息通道。游戏遮罩和配置窗口各自持有实例。
/// </summary>
public sealed class HtmlMessageChannel
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ConcurrentDictionary<string, ConcurrentQueue<HtmlMessage>> _toHtmlQueues = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<HtmlMessage>> _fromHtmlQueues = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, string> _requestWindows = new();
    private readonly ConcurrentDictionary<string, Action<string>> _flushers = new();

    public void Register(string windowId, Action<string> flush)
    {
        _toHtmlQueues[windowId] = new ConcurrentQueue<HtmlMessage>();
        _fromHtmlQueues[windowId] = new ConcurrentQueue<HtmlMessage>();
        _flushers[windowId] = flush;
    }

    public void Unregister(string windowId)
    {
        _flushers.TryRemove(windowId, out _);
        Cleanup(windowId);
    }

    public bool Exists(string windowId) => _flushers.ContainsKey(windowId);

    public void Send(string windowId, string url, string jsonData)
    {
        Enqueue(windowId, url, jsonData, null);
    }

    public void Respond(string windowId, string requestId, string jsonData)
    {
        if (string.IsNullOrWhiteSpace(requestId)) throw new ArgumentException("requestId cannot be empty", nameof(requestId));
        Enqueue(windowId, "", jsonData, requestId);
    }

    public async Task<string> Request(string windowId, string url, string jsonData, int timeoutMs)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[requestId] = completion;
        _requestWindows[requestId] = windowId;
        try
        {
            Enqueue(windowId, url, jsonData, requestId);
            if (timeoutMs <= 0) return await completion.Task;
            var completed = await Task.WhenAny(completion.Task, Task.Delay(timeoutMs));
            if (completed != completion.Task) throw new TimeoutException($"HTML请求超时: {url}");
            return await completion.Task;
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
            _requestWindows.TryRemove(requestId, out _);
        }
    }

    public async Task<string?> Receive(string windowId, int timeoutMs)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            if (_fromHtmlQueues.TryGetValue(windowId, out var queue) && queue.TryDequeue(out var message))
                return JsonSerializer.Serialize(message, JsonOptions);
            if (!Exists(windowId)) return null;
            if (timeoutMs > 0 && watch.ElapsedMilliseconds > timeoutMs) return null;
            await Task.Delay(15);
        }
    }

    public string? Poll(string windowId)
    {
        return _fromHtmlQueues.TryGetValue(windowId, out var queue) && queue.TryDequeue(out var message)
            ? JsonSerializer.Serialize(message, JsonOptions)
            : null;
    }

    public string PollAll(string windowId)
    {
        var messages = new List<HtmlMessage>();
        if (_fromHtmlQueues.TryGetValue(windowId, out var queue))
            while (queue.TryDequeue(out var message)) messages.Add(message);
        return JsonSerializer.Serialize(messages, JsonOptions);
    }

    public void SendFromHtml(string windowId, string url, string data, string? requestId)
    {
        if (requestId != null && _pendingRequests.TryRemove(requestId, out var completion))
        {
            var parsed = Parse(data);
            completion.TrySetResult(parsed?.GetRawText() ?? "null");
            return;
        }

        if (_fromHtmlQueues.TryGetValue(windowId, out var queue))
        {
            queue.Enqueue(new HtmlMessage { Url = url, Data = Parse(data), RequestId = requestId });
        }
    }

    public void Flush(string windowId, Action<string> post)
    {
        if (!_toHtmlQueues.TryGetValue(windowId, out var queue)) return;
        while (queue.TryDequeue(out var message)) post(JsonSerializer.Serialize(message, JsonOptions));
    }

    private void Enqueue(string windowId, string url, string jsonData, string? requestId)
    {
        if (!Exists(windowId) || !_toHtmlQueues.TryGetValue(windowId, out var queue))
            throw new InvalidOperationException($"HTML窗口不存在或已关闭: {windowId}");
        queue.Enqueue(new HtmlMessage { Url = url, Data = Parse(jsonData), RequestId = requestId });
        if (_flushers.TryGetValue(windowId, out var flush)) flush(windowId);
    }

    private void Cleanup(string windowId)
    {
        _toHtmlQueues.TryRemove(windowId, out _);
        _fromHtmlQueues.TryRemove(windowId, out _);
        foreach (var request in _requestWindows.Where(item => item.Value == windowId))
        {
            if (_pendingRequests.TryRemove(request.Key, out var completion)) completion.TrySetCanceled();
            _requestWindows.TryRemove(request.Key, out _);
        }
    }

    private static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch
        {
            return JsonSerializer.SerializeToElement(json, JsonOptions);
        }
    }
}

public sealed class HtmlMessage
{
    public string Url { get; set; } = "";
    public JsonElement? Data { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestId { get; set; }
}
