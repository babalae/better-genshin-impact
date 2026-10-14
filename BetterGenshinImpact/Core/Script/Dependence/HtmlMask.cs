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

    private static readonly HtmlMessageChannel Channel = new();

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
            Channel.Register(windowId, HtmlMaskWindow.NotifyFlush);
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
        Channel.Unregister(id);
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
            Channel.Unregister(windowId);
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

    /// <summary>
    /// 设置窗口的点击穿透模式
    /// </summary>
    /// <param name="windowId">窗口ID</param>
    /// <param name="enabled">true=点击穿透，false=可交互</param>
    public void SetClickThrough(string windowId, bool enabled)
    {
        HtmlMaskWindow.SetClickThrough(windowId, enabled);
    }

    /// <summary>
    /// 获取窗口的点击穿透状态
    /// </summary>
    /// <param name="windowId">窗口ID</param>
    /// <returns>true=点击穿透，false=可交互</returns>
    public bool GetClickThrough(string windowId)
    {
        return HtmlMaskWindow.GetClickThrough(windowId);
    }

    /// <summary>
    /// 切换窗口的点击穿透模式
    /// </summary>
    /// <param name="windowId">窗口ID</param>
    public void ToggleClickThrough(string windowId)
    {
        HtmlMaskWindow.ToggleClickThrough(windowId);
    }

    #endregion

    #region 消息通信

    /// <summary>
    /// 发送消息到HTML（单向推送）
    /// </summary>
    public void Send(string windowId, string url, string jsonData)
    {
        Channel.Send(windowId, url, jsonData);
    }

    /// <summary>
    /// 响应 HTML 页面通过 window.htmlMask.request(...) 发起的请求。
    /// </summary>
    /// <param name="windowId">HTML 遮罩窗口 ID。</param>
    /// <param name="requestId">从 Receive/Poll/PollAll 返回消息的 requestId 字段取得，不能由脚本自行生成。</param>
    /// <param name="jsonData">响应数据，必须是合法 JSON 字符串。</param>
    public void Respond(string windowId, string requestId, string jsonData)
    {
        Channel.Respond(windowId, requestId, jsonData);
    }
    
    /// <summary>
    /// 发送请求到HTML并等待响应
    /// </summary>
    /// <param name="windowId">目标窗口ID</param>
    /// <param name="url">接口路径</param>
    /// <param name="jsonData">JSON数据</param>
    /// <param name="timeoutMs">超时毫秒，0表示无限等待</param>
    public Task<string?> Request(string windowId, string url, string jsonData, int timeoutMs = 0)
    {
        return Channel.Request(windowId, url, jsonData, timeoutMs);
    }

    /// <summary>
    /// 等待接收来自HTML的一条消息
    /// </summary>
    /// <param name="windowId">窗口ID</param>
    /// <param name="timeoutMs">超时毫秒，0表示无限等待</param>
    public Task<string?> Receive(string windowId, int timeoutMs = 0)
    {
        return Channel.Receive(windowId, timeoutMs);
    }

    /// <summary>
    /// 轮询来自HTML的消息（非阻塞）
    /// </summary>
    public string? Poll(string windowId)
    {
        return Channel.Poll(windowId);
    }

    /// <summary>
    /// 批量获取来自HTML的所有消息
    /// </summary>
    public string PollAll(string windowId)
    {
        return Channel.PollAll(windowId);
    }

    #endregion

    #region 内部方法

    internal static void FlushPendingMessages(string windowId, Action<string> postAction)
    {
        Channel.Flush(windowId, postAction);
    }

    internal static void SendFromHtml(string windowId, string url, string data, string? requestId = null)
    {
        Channel.SendFromHtml(windowId, url, data, requestId);
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseAll();
    }
}
