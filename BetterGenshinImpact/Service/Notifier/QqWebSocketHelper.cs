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
/// QQ official WebSocket client used to bind a C2C OpenID.
/// The C2C OpenID cannot be queried via REST API; it is only delivered
/// passively through the gateway when the user sends a private message
/// (C2C_MESSAGE_CREATE) or adds the bot as a friend (FRIEND_ADD).
/// </summary>
public class QqWebSocketHelper
{
    private static readonly ILogger Logger = App.GetLogger<QqWebSocketHelper>();

    private const string TokenUrl = "https://bots.qq.com/app/getAppAccessToken";
    private const string GatewayUrl = "https://api.bot.qq.com/gateway";
    private const int Intents = 33554432; // 1 &lt;&lt; 25, GROUP_AND_C2C_EVENT
    private const int BindTimeoutSeconds = 60;

    /// <summary>
    /// Connects to the QQ gateway and waits until the user's C2C OpenID is
    /// observed. Returns the OpenID, or throws <see cref="NotifierException"/>
    /// on timeout / cancellation / protocol error.
    /// </summary>
    public static async Task<string> BindAsync(string appId, string clientSecret, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new NotifierException("QQ AppID is empty");

        if (string.IsNullOrWhiteSpace(clientSecret))
            throw new NotifierException("QQ AppSecret is empty");

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(BindTimeoutSeconds));
        var ct = timeoutCts.Token;

        var accessToken = await GetAccessTokenAsync(httpClient, appId, clientSecret, ct);
        var gatewayUrl = await GetGatewayUrlAsync(httpClient, accessToken, ct);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(gatewayUrl), ct);

        var heartbeatInterval = await ReceiveHelloAsync(socket, ct);
        await SendIdentifyAsync(socket, accessToken, ct);

        // Heartbeat runs in the background for the lifetime of the connection.
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeatTask = RunHeartbeatAsync(socket, heartbeatInterval, heartbeatCts.Token);

        try
        {
            return await ReceiveUntilOpenIdAsync(socket, ct);
        }
        finally
        {
            heartbeatCts.Cancel();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when the bind flow finishes.
            }
        }
    }

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

    private static async Task<int> ReceiveHelloAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var payload = await ReceiveMessageAsync(socket, ct);
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var op = root.GetProperty("op").GetInt32();
        if (op != 10)
            throw new NotifierException($"Unexpected gateway opcode {op}, expected Hello (10)");

        if (root.TryGetProperty("d", out var d) && d.TryGetProperty("heartbeat_interval", out var interval))
            return interval.GetInt32();

        return 45000;
    }

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

    private static async Task RunHeartbeatAsync(ClientWebSocket socket, int heartbeatInterval, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(heartbeatInterval, ct);
                var payload = JsonSerializer.Serialize(new { op = 1, d = (int?)null });
                await SendMessageAsync(socket, payload, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning("QQ heartbeat loop stopped: {ex}", ex.Message);
        }
    }

    private static async Task<string> ReceiveUntilOpenIdAsync(ClientWebSocket socket, CancellationToken ct)
    {
        while (true)
        {
            var payload = await ReceiveMessageAsync(socket, ct);
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (!root.TryGetProperty("op", out var opElement))
                continue;

            var op = opElement.GetInt32();

            // Invalid Session: the bot lacks permission for the requested intents.
            if (op == 9)
                throw new NotifierException("QQ bot does not have permission for C2C events (intents)");

            if (op != 0)
                continue;

            if (!root.TryGetProperty("t", out var tElement))
                continue;

            var eventType = tElement.GetString();
            if (!root.TryGetProperty("d", out var d))
                continue;

            var openId = eventType switch
            {
                "C2C_MESSAGE_CREATE" => ExtractOpenId(d, "author", "user_openid"),
                "FRIEND_ADD" => ExtractOpenId(d, "openid"),
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(openId))
                return openId;
        }
    }

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

    private static async Task<string> ReceiveMessageAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new System.IO.MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new NotifierException("QQ gateway closed the connection");

            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static async Task SendMessageAsync(ClientWebSocket socket, string payload, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }
}
