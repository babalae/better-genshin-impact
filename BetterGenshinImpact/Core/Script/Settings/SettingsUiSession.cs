using System;
using System.Text.Json;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.Core.Script.Settings;

/// <summary>
/// 配置会话的独立消息对象。只绑定当前这一个普通窗口，不接受窗口 ID，也不是 htmlMask 的别名。
/// </summary>
public sealed class SettingsUiSession(HtmlMessageChannel channel, string windowId, Func<bool> isOpen, Func<bool> close)
{
    private readonly HtmlMessageChannel _channel = channel;

    public bool IsOpen() => isOpen();

    public bool Close() => close();

    public void Post(string url, object? data) => _channel.Send(windowId, url, ToJson(data));

    public Task<string?> Ask(string url, object? data, int timeoutMs = 0)
        => _channel.Request(windowId, url, ToJson(data), timeoutMs);

    public void Reply(string requestId, object? data) => _channel.Respond(windowId, requestId, ToJson(data));

    private static string ToJson(object? data) => data switch
    {
        null => "null",
        string json => json,
        _ => JsonSerializer.Serialize(data, ConfigService.JsonOptions)
    };

    public Task<string?> Receive(int timeoutMs = 0) => _channel.Receive(windowId, timeoutMs);

    public string? Poll() => _channel.Poll(windowId);

    public string PollAll() => _channel.PollAll(windowId);
}
