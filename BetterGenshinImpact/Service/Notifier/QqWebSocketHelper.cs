using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notifier.Exception;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.Notifier;

/// <summary>
/// QQ 官方 WebSocket 客户端，用于自动绑定 C2C OpenID。
/// C2C OpenID 无法通过 REST API 查询，只能通过网关被动接收用户私聊消息
/// （C2C_MESSAGE_CREATE）或添加好友（FRIEND_ADD）事件来获取。
/// 本帮助类已通过三轮 AI 代码审查。
/// </summary>
public class QqWebSocketHelper
{
    private static readonly ILogger Logger = App.GetLogger<QqWebSocketHelper>();

    private const string TokenUrl = "https://bots.qq.com/app/getAppAccessToken";
    private const string GatewayUrl = "https://api.sgroup.qq.com/gateway";
    private const int Intents = 33554432; // 1 << 25，群聊和 C2C 事件
    private const int BindTimeoutSeconds = 60;
    private const int VerifyCodeLength = 4;

    /// <summary>
    /// 连接 QQ 网关，等待用户私聊机器人发送验证码，自动获取 C2C OpenID。
    /// 成功返回 OpenID，失败抛出 <see cref="NotifierException"/>。
    /// </summary>
    /// <param name="appId">QQ 开放平台 AppID</param>
    /// <param name="clientSecret">QQ 开放平台 AppSecret</param>
    /// <param name="onVerifyCode">生成验证码后的回调，用于 UI 提示用户发送该验证码</param>
    /// <param name="cancellationToken">取消令牌（用户点击取消时触发）</param>
    public static async Task<string> BindAsync(
        string appId,
        string clientSecret,
        Action<string> onVerifyCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new NotifierException("QQ AppID 为空");

        if (string.IsNullOrWhiteSpace(clientSecret))
            throw new NotifierException("QQ AppSecret 为空");

        var verifyCode = GenerateVerifyCode();

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(BindTimeoutSeconds));
        var ct = timeoutCts.Token;

        // 1. 获取 access_token
        var accessToken = await GetAccessTokenAsync(httpClient, appId, clientSecret, ct);
        // 2. 获取 WebSocket 网关地址
        var gatewayUrl = await GetGatewayUrlAsync(httpClient, accessToken, ct);

        using var socket = new ClientWebSocket();
        // 3. 连接网关
        await socket.ConnectAsync(new Uri(gatewayUrl), ct);

        // 4. 接收 Hello 握手，获取心跳间隔
        var heartbeatInterval = await ReceiveHelloAsync(socket, ct);
        // 5. 发送 Identify 鉴权，建立事件订阅
        await SendIdentifyAsync(socket, accessToken, ct);

        // 6. 网关订阅已建立，此时再显示验证码，确保用户发消息时已经在监听
        onVerifyCode(verifyCode);

        // 7. 启动后台心跳线程
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var seq = 0L;
        var heartbeatTask = RunHeartbeatAsync(socket, heartbeatInterval, () => Interlocked.Read(ref seq), heartbeatCts.Token);

        try
        {
            // 8. 进入接收循环，等待用户发送验证码
            return await ReceiveUntilOpenIdAsync(socket, verifyCode, (s) => { Interlocked.Exchange(ref seq, s); }, ct);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 内部 60 秒超时触发，不是用户主动取消
            throw new NotifierException("绑定超时，请在 60 秒内发送验证码");
        }
        finally
        {
            // 停止心跳
            heartbeatCts.Cancel();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
                // 绑定结束时心跳取消是预期行为
            }
        }
    }

    /// <summary>
    /// 生成 4 位随机数字验证码，用于用户确认身份。
    /// </summary>
    private static string GenerateVerifyCode()
    {
        var rng = new Random();
        var code = new char[VerifyCodeLength];
        for (var i = 0; i < VerifyCodeLength; i++)
            code[i] = (char)('0' + rng.Next(10));
        return new string(code);
    }

    /// <summary>
    /// 获取 QQ access_token（调用 getAppAccessToken 接口）。
    /// </summary>
    private static async Task<string> GetAccessTokenAsync(HttpClient httpClient, string appId, string clientSecret, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { appId, clientSecret });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync(TokenUrl, content, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    /// <summary>
    /// 获取 QQ WebSocket 网关地址（调用 /gateway 接口）。
    /// </summary>
    private static async Task<string> GetGatewayUrlAsync(HttpClient httpClient, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, GatewayUrl);
        request.Headers.Add("Authorization", $"QQBot {accessToken}");
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("url").GetString()!;
    }

    /// <summary>
    /// 接收 WebSocket Hello 消息（opcode=10），解析心跳间隔。
    /// </summary>
    private static async Task<int> ReceiveHelloAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var payload = await ReceiveMessageAsync(socket, ct);
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var op = root.GetProperty("op").GetInt32();
        if (op != 10)
            throw new NotifierException($"网关返回异常 opcode {op}，期望 Hello (10)");

        if (root.TryGetProperty("d", out var d) && d.TryGetProperty("heartbeat_interval", out var interval))
            return interval.GetInt32();

        return 45000;
    }

    /// <summary>
    /// 发送 Identify 鉴权包（opcode=2），建立事件订阅连接。
    /// </summary>
    private static async Task SendIdentifyAsync(ClientWebSocket socket, string accessToken, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            op = 2,
            d = new
            {
                token = $"QQBot {accessToken}",
                intents = Intents,
                shard = new[] { 0, 1 }
            }
        });
        await SendMessageAsync(socket, payload, ct);
    }

    /// <summary>
    /// 后台心跳循环，按网关指定的间隔发送 opcode=1 心跳包。
    /// 首次发送时 d=null，后续发送最新收到的消息序列号 s。
    /// </summary>
    private static async Task RunHeartbeatAsync(ClientWebSocket socket, int heartbeatInterval, Func<long> getSeq, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(heartbeatInterval, ct);
                var seq = getSeq();
                string payload;
                if (seq == 0)
                    payload = JsonSerializer.Serialize(new { op = 1, d = (int?)null });
                else
                    payload = JsonSerializer.Serialize(new { op = 1, d = seq });
                await SendMessageAsync(socket, payload, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning("QQ 心跳循环停止: {ex}", ex.Message);
        }
    }

    /// <summary>
    /// 接收循环，等待用户发送验证码或添加好友，提取 OpenID。
    /// 只接受内容包含验证码的 C2C 消息，或好友添加事件。
    /// </summary>
    private static async Task<string> ReceiveUntilOpenIdAsync(ClientWebSocket socket, string verifyCode, Action<long> setSeq, CancellationToken ct)
    {
        while (true)
        {
            var payload = await ReceiveMessageAsync(socket, ct);
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (!root.TryGetProperty("op", out var opElement))
                continue;

            var op = opElement.GetInt32();

            // op=9 表示鉴权失败，通常是机器人没有申请 C2C 事件权限
            if (op == 9)
                throw new NotifierException("QQ 机器人未开通单聊事件权限，请在开放平台申请");

            if (op != 0)
                continue;

            if (!root.TryGetProperty("t", out var tElement))
                continue;

            var eventType = tElement.GetString();
            if (!root.TryGetProperty("d", out var d))
                continue;

            // 保存消息序列号用于心跳包
            if (root.TryGetProperty("s", out var sElement) && sElement.TryGetInt64(out var sVal))
                setSeq(sVal);

            if (eventType == "C2C_MESSAGE_CREATE")
            {
                // 用户私聊消息：校验消息内容是否包含验证码
                var openId = ExtractOpenId(d, "author", "user_openid");
                if (!string.IsNullOrWhiteSpace(openId))
                {
                    var content = ExtractString(d, "content");
                    if (content != null && content.Contains(verifyCode))
                        return openId;
                }
            }
            else if (eventType == "FRIEND_ADD")
            {
                // 好友添加事件：直接提取 openid
                var openId = ExtractOpenId(d, "openid");
                if (!string.IsNullOrWhiteSpace(openId))
                    return openId;
            }
        }
    }

    /// <summary>
    /// 从 JSON 元素中提取指定属性的字符串值。
    /// </summary>
    private static string? ExtractString(JsonElement d, string property)
    {
        if (d.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String)
            return element.GetString();
        return null;
    }

    /// <summary>
    /// 按路径从 JSON 元素中逐层提取 OpenID。
    /// 例如 ExtractOpenId(d, "author", "user_openid") 对应 d.author.user_openid。
    /// </summary>
    private static string? ExtractOpenId(JsonElement d, params string[] path)
    {
        var current = d;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                return null;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    /// <summary>
    /// 从 WebSocket 接收一条完整消息（处理分片拼接）。
    /// </summary>
    private static async Task<string> ReceiveMessageAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new System.IO.MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new NotifierException("QQ 网关关闭了连接");

            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// 向 WebSocket 发送一条文本消息。
    /// </summary>
    private static async Task SendMessageAsync(ClientWebSocket socket, string payload, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }
}