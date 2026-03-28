using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
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
    }

    /// <summary>
    /// 脚本到HTML的待推送队列
    /// </summary>
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<Message>> _toHtmlQueues = new();

    /// <summary>
    /// HTML到脚本的消息队列
    /// </summary>
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<Message>> _fromHtmlQueues = new();

    private readonly string _workDir;
    private readonly List<string> _openedWindows = [];
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
            _openedWindows.Add(windowId);

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
        _openedWindows.Remove(id);
        CleanupQueues(id);
        return HtmlMaskWindow.Close(id);
    }

    /// <summary>
    /// 关闭所有由本实例打开的窗口
    /// </summary>
    public void CloseAll()
    {
        foreach (var windowId in _openedWindows)
        {
            CleanupQueues(windowId);
            HtmlMaskWindow.Close(windowId);
        }
        _openedWindows.Clear();
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
    /// 发送消息到HTML
    /// </summary>
    /// <param name="windowId">目标窗口ID</param>
    /// <param name="url">接口路径，如 /data/update</param>
    /// <param name="jsonData">JSON数据</param>
    public void Send(string windowId, string url, string jsonData)
    {
        if (!_toHtmlQueues.TryGetValue(windowId, out var queue))
        {
            _toHtmlQueues[windowId] = queue = new ConcurrentQueue<Message>();
        }

        queue.Enqueue(new Message
        {
            Url = url,
            Data = ParseData(jsonData)
        });

        // 通知WebView2推送
        HtmlMaskWindow.NotifyFlush(windowId);
    }

    /// <summary>
    /// 轮询来自HTML的消息
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
    /// HTML端发来的消息入队
    /// </summary>
    internal static void SendFromHtml(string windowId, string url, string data)
    {
        if (_fromHtmlQueues.TryGetValue(windowId, out var queue))
        {
            queue.Enqueue(new Message
            {
                Url = url,
                Data = ParseData(data)
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
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseAll();
    }
}
