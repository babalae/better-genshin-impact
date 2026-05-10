using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Utils;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.View;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Script.Dependence;

/// <summary>
/// HTML遮罩层 - JS脚本依赖类
/// 提供窗口管理与消息通信功能
/// </summary>
public class HtmlMask : IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// 通信消息结构
    /// </summary>
    public class Message
    {
        public string Url { get; set; } = "";
        public JsonElement? Data { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RequestId { get; set; }
    }

    /// <summary>
    /// 脚本到HTML的待推送队列
    /// </summary>
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<Message>> _toHtmlQueues = new();

    /// <summary>
    /// HTML到脚本的消息队列
    /// </summary>
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<Message>> _fromHtmlQueues = new();

    /// <summary>
    /// JS到HTML请求的等待句柄，用于request-response匹配
    /// </summary>
    private static readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _jsPendingRequests = new();

    /// <summary>
    /// requestId到windowId的映射，用于窗口关闭时取消对应的pending请求
    /// </summary>
    private static readonly ConcurrentDictionary<string, string> _requestWindowMap = new();

    private readonly string _workDir;
    private readonly List<string> _openedWindows = [];
    private readonly object _openedWindowsLock = new();
    private bool _disposed;

    public HtmlMask(string workDir)
    {
        _workDir = workDir;
    }

    #region 窗口管理

    /// <summary>
    /// 显示HTML遮罩窗口
    /// </summary>
    public string Show(string url, string? id = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("URL不能为空");

            string finalUrl;

            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                finalUrl = url;
            }
            else
            {
                // 禁止 file:// 绝对路径，仅允许脚本目录下的相对路径
                string absPath = ScriptUtils.NormalizePath(_workDir, url);
                finalUrl = new Uri(absPath).AbsoluteUri;
            }

            string windowId = HtmlMaskWindow.Show(finalUrl, id, _workDir);

            _toHtmlQueues[windowId] = new ConcurrentQueue<Message>();
            _fromHtmlQueues[windowId] = new ConcurrentQueue<Message>();
            lock (_openedWindowsLock) { _openedWindows.Add(windowId); }

            return windowId;
        }
        catch (Exception ex)
        {
            TaskControl.Logger.LogError(ex, "打开HTML遮罩失败: {Url}", url);
            throw;
        }
    }

    /// <summary>
    /// 关闭指定窗口
    /// </summary>
    public bool Close(string id)
    {
        lock (_openedWindowsLock) { _openedWindows.Remove(id); }
        CleanupQueues(id);
        return HtmlMaskWindow.Close(id);
    }

    /// <summary>
    /// 关闭所有由本实例打开的窗口
    /// </summary>
    public void CloseAll()
    {
        List<string> windows;
        lock (_openedWindowsLock)
        {
            windows = [.. _openedWindows];
            _openedWindows.Clear();
        }
        foreach (var windowId in windows)
        {
            CleanupQueues(windowId);
            HtmlMaskWindow.Close(windowId);
        }
    }

    /// <summary>
    /// 获取所有窗口ID
    /// </summary>
    public string[] GetWindowIds() => HtmlMaskWindow.GetWindowIds();

    /// <summary>
    /// 窗口是否存在
    /// </summary>
    public bool Exists(string id) => HtmlMaskWindow.Exists(id);

    #endregion

    #region 消息通信

    /// <summary>
    /// 发送消息到HTML（单向推送）
    /// </summary>
    public void Send(string windowId, string url, string jsonData)
    {
        if (!HtmlMaskWindow.Exists(windowId) || !_toHtmlQueues.TryGetValue(windowId, out var queue))
            throw new InvalidOperationException($"HTML遮罩窗口不存在或已关闭: {windowId}");

        queue.Enqueue(new Message
        {
            Url = url,
            Data = ParseData(jsonData)
        });

        HtmlMaskWindow.NotifyFlush(windowId);
    }

    /// <summary>
    /// 发送请求到HTML并等待响应
    /// </summary>
    /// <param name="windowId">目标窗口ID</param>
    /// <param name="url">接口路径</param>
    /// <param name="jsonData">JSON数据</param>
    /// <param name="timeoutMs">超时毫秒，0表示无限等待</param>
    public async Task<string?> Request(string windowId, string url, string jsonData, int timeoutMs = 0)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _jsPendingRequests[requestId] = tcs;
        _requestWindowMap[requestId] = windowId;

        try
        {
            if (!HtmlMaskWindow.Exists(windowId) || !_toHtmlQueues.TryGetValue(windowId, out var queue))
                throw new InvalidOperationException($"HTML遮罩窗口不存在或已关闭: {windowId}");

            queue.Enqueue(new Message
            {
                Url = url,
                Data = ParseData(jsonData),
                RequestId = requestId
            });

            HtmlMaskWindow.NotifyFlush(windowId);

            if (timeoutMs > 0)
            {
                using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
                await using var registration = cts.Token.Register(() =>
                {
                    if (_jsPendingRequests.TryRemove(requestId, out var pending))
                        pending.TrySetResult(null!);
                });
                return await tcs.Task;
            }

            return await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _jsPendingRequests.TryRemove(requestId, out _);
            _requestWindowMap.TryRemove(requestId, out _);
        }
    }

    /// <summary>
    /// 等待接收来自HTML的一条消息
    /// </summary>
    /// <param name="windowId">窗口ID</param>
    /// <param name="timeoutMs">超时毫秒，0表示无限等待</param>
    public async Task<string?> Receive(string windowId, int timeoutMs = 0)
    {
        if (!_fromHtmlQueues.TryGetValue(windowId, out var queue))
            return null;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            if (queue.TryDequeue(out var message))
                return JsonSerializer.Serialize(message, _jsonOptions);

            if (_disposed || !_fromHtmlQueues.ContainsKey(windowId) || !HtmlMaskWindow.Exists(windowId))
                return null;

            if (timeoutMs > 0 && sw.ElapsedMilliseconds > timeoutMs)
                return null;

            await Task.Delay(50);
        }
    }

    /// <summary>
    /// 轮询来自HTML的消息（非阻塞）
    /// </summary>
    public string? Poll(string windowId)
    {
        if (_fromHtmlQueues.TryGetValue(windowId, out var queue) &&
            queue.TryDequeue(out var message))
        {
            return JsonSerializer.Serialize(message, _jsonOptions);
        }
        return null;
    }

    /// <summary>
    /// 批量获取来自HTML的所有消息
    /// </summary>
    public string PollAll(string windowId)
    {
        var messages = new List<Message>();
        if (_fromHtmlQueues.TryGetValue(windowId, out var queue))
        {
            while (queue.TryDequeue(out var message))
            {
                messages.Add(message);
            }
        }
        return JsonSerializer.Serialize(messages, _jsonOptions);
    }

    #endregion

    #region 内部方法

    /// <summary>
    /// 将待推送队列中的消息通过回调逐一发出
    /// </summary>
    internal static void FlushPendingMessages(string windowId, Action<string> postAction)
    {
        if (_toHtmlQueues.TryGetValue(windowId, out var queue))
        {
            while (queue.TryDequeue(out var msg))
            {
                postAction(JsonSerializer.Serialize(msg, _jsonOptions));
            }
        }
    }

    /// <summary>
    /// HTML端发来的消息入队，如果是JS请求的响应则直接resolve
    /// </summary>
    internal static void SendFromHtml(string windowId, string url, string data, string? requestId = null)
    {
        // 匹配JS端pending的request
        if (requestId != null && _jsPendingRequests.TryRemove(requestId, out var tcs))
        {
            var parsed = ParseData(data);
            tcs.TrySetResult(parsed != null ? parsed.Value.GetRawText() : "null");
            return;
        }

        // 普通消息入队
        if (_fromHtmlQueues.TryGetValue(windowId, out var queue))
        {
            queue.Enqueue(new Message
            {
                Url = url,
                Data = ParseData(data),
                RequestId = requestId
            });
        }
    }

    private static JsonElement? ParseData(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch
        {
            // 不是合法JSON，作为纯文本包装
            return JsonSerializer.SerializeToElement(json, _jsonOptions);
        }
    }

    private static void CleanupQueues(string windowId)
    {
        _toHtmlQueues.TryRemove(windowId, out _);
        _fromHtmlQueues.TryRemove(windowId, out _);

        // 取消该窗口关联的待响应请求
        foreach (var kvp in _requestWindowMap)
        {
            if (kvp.Value == windowId && _jsPendingRequests.TryRemove(kvp.Key, out var tcs))
            {
                tcs.TrySetCanceled();
            }
        }
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseAll();
    }
}
