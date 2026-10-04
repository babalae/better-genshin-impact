using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notifier.Exception;

namespace BetterGenshinImpact.Service.Notifier;

/// <summary>
/// Discord 服务器信息
/// </summary>
public sealed record DiscordGuildInfo(string Id, string Name);

/// <summary>
/// Discord 频道信息
/// </summary>
public sealed record DiscordChannelInfo(string Id, string Name, int Type, string? ParentId, int Position);

/// <summary>
/// Discord 服务器成员信息
/// </summary>
public sealed record DiscordMemberInfo(string Id, string DisplayName);

/// <summary>
/// Discord 频道类型常量
/// </summary>
public static class DiscordChannelTypes
{
    public const int GuildText = 0;
    public const int GuildVoice = 2;
    public const int GuildCategory = 4;
    public const int GuildAnnouncement = 5;
    public const int GuildStageVoice = 13;
    public const int GuildForum = 15;

    /// <summary>
    /// 该类型的频道是否可以直接发送消息。
    /// 论坛（15）需要先建立帖子，语音（2）不是消息频道，分类（4）只是分组标题。
    /// </summary>
    public static bool CanSendMessage(int type)
    {
        return type is GuildText or GuildAnnouncement;
    }
}

/// <summary>
/// Discord 机器人 REST API 客户端：用于列出服务器/频道/成员，以及解析私信频道。
/// ref: https://discord.com/developers/docs/reference
/// </summary>
public sealed class DiscordBotApiClient
{
    private const string ApiBase = "https://discord.com/api/v10";

    /// <summary>
    /// 单次拉取成员数量上限（Discord 允许的最大值）
    /// </summary>
    private const int MemberPageSize = 1000;

    /// <summary>
    /// 成员最多翻页次数，避免超大服务器把界面卡死
    /// </summary>
    private const int MaxMemberPages = 10;

    private readonly HttpClient _httpClient;
    private readonly string _botToken;

    /// <summary>
    /// 使用共享的 HttpClient 与机器人 Token 创建客户端，Token 会去掉首尾空白。
    /// </summary>
    public DiscordBotApiClient(HttpClient httpClient, string botToken)
    {
        _httpClient = httpClient;
        _botToken = botToken?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// 列出机器人所在的服务器。
    /// </summary>
    public async Task<List<DiscordGuildInfo>> GetGuildsAsync(CancellationToken ct = default)
    {
        using var root = await SendAsync(HttpMethod.Get, "/users/@me/guilds", null, ct);
        var result = new List<DiscordGuildInfo>();
        foreach (var item in root.RootElement.EnumerateArray())
        {
            result.Add(new DiscordGuildInfo(
                GetString(item, "id"),
                GetString(item, "name")));
        }

        return result;
    }

    /// <summary>
    /// 列出指定服务器的所有频道（含分类节点，供上层做两级分组）。
    /// </summary>
    public async Task<List<DiscordChannelInfo>> GetGuildChannelsAsync(string guildId, CancellationToken ct = default)
    {
        using var root = await SendAsync(HttpMethod.Get, $"/guilds/{guildId}/channels", null, ct);
        var result = new List<DiscordChannelInfo>();
        foreach (var item in root.RootElement.EnumerateArray())
        {
            result.Add(new DiscordChannelInfo(
                GetString(item, "id"),
                GetString(item, "name"),
                item.TryGetProperty("type", out var type) && type.TryGetInt32(out var typeValue) ? typeValue : 0,
                item.TryGetProperty("parent_id", out var parent) && parent.ValueKind == JsonValueKind.String
                    ? parent.GetString()
                    : null,
                item.TryGetProperty("position", out var position) && position.TryGetInt32(out var positionValue)
                    ? positionValue
                    : 0));
        }

        return result;
    }

    /// <summary>
    /// 列出指定服务器的成员，用于挑选私信对象。
    /// 需要机器人开启 GUILD_MEMBERS 特权 intent，否则 Discord 会回 403。
    /// </summary>
    public async Task<List<DiscordMemberInfo>> GetGuildMembersAsync(string guildId, CancellationToken ct = default)
    {
        var result = new List<DiscordMemberInfo>();
        var after = string.Empty;

        for (var page = 0; page < MaxMemberPages; page++)
        {
            var path = $"/guilds/{guildId}/members?limit={MemberPageSize}";
            if (!string.IsNullOrEmpty(after))
            {
                path += $"&after={after}";
            }

            using var root = await SendAsync(HttpMethod.Get, path, null, ct, MemberAccessHint);
            var count = 0;
            foreach (var item in root.RootElement.EnumerateArray())
            {
                count++;
                var user = item.TryGetProperty("user", out var userElement) ? userElement : default;
                var id = GetString(user, "id");
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                after = id;
                result.Add(new DiscordMemberInfo(id, BuildMemberDisplayName(item, user)));
            }

            // 不足一页说明已经取完
            if (count < MemberPageSize)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// 取得（或建立）与指定用户的私信频道，返回频道 ID。
    /// Discord 对同一个用户只会有一个私信频道，因此结果可以长期缓存。
    /// </summary>
    public async Task<string> CreateDirectMessageChannelAsync(string userId, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { recipient_id = userId });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var root = await SendAsync(
            HttpMethod.Post,
            "/users/@me/channels",
            content,
            ct,
            "无法与该用户建立私信，请确认对方与机器人在同一个服务器");
        return GetString(root.RootElement, "id");
    }

    private const string MemberAccessHint =
        "无法读取成员列表：请在 Discord 开发者后台 → Bot → Privileged Gateway Intents 开启 SERVER MEMBERS INTENT，或改用「手动输入用户 ID」";

    /// <summary>
    /// 发送请求并解析 JSON 响应，失败时抛出带 Discord 错误信息的异常。
    /// </summary>
    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken ct,
        string? failureHint = null)
    {
        if (string.IsNullOrWhiteSpace(_botToken))
        {
            throw new NotifierException("Discord 机器人 Token 未设置");
        }

        using var request = new HttpRequestMessage(method, ApiBase + path) { Content = content };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bot {_botToken}");
        using var response = await _httpClient.SendAsync(request, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = ExtractErrorMessage(body) ?? response.ReasonPhrase ?? "未知错误";
            var hint = failureHint == null ? string.Empty : $"。{failureHint}";
            throw new NotifierException($"Discord API 请求失败 ({(int)response.StatusCode})：{detail}{hint}");
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new NotifierException($"Discord 返回内容无法解析：{ex.Message}");
        }
    }

    /// <summary>
    /// 从 Discord 错误响应体里取出可读的 message 字段，供通知器复用。
    /// </summary>
    public static string? ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("message", out var message) ? message.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 读取 JSON 对象中的字符串属性。元素不是对象、属性不存在或类型不符时返回空字符串。
    /// </summary>
    private static string GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    /// <summary>
    /// 成员显示名优先级：服务器昵称 → 全局显示名 → 用户名。
    /// </summary>
    private static string BuildMemberDisplayName(JsonElement member, JsonElement user)
    {
        var nick = GetString(member, "nick");
        if (!string.IsNullOrWhiteSpace(nick))
        {
            return nick;
        }

        var globalName = GetString(user, "global_name");
        if (!string.IsNullOrWhiteSpace(globalName))
        {
            return globalName;
        }

        var username = GetString(user, "username");
        return string.IsNullOrWhiteSpace(username) ? "未知用户" : username;
    }
}
