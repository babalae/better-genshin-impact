using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notifier.Exception;

namespace BetterGenshinImpact.Service.Notifier;

/// <summary>
/// 微信 Clawbot（iLink 协议）帮助类，BetterGI 内置实现，不依赖 OpenClaw。
///
/// 完整流程：
/// 1. 登录（get_bot_qrcode → 轮询 get_qrcode_status）→ 获取 bot_token、base_url、用户 ID
/// 2. 绑定（长轮询 getupdates → 用户发送一次性验证码 → 捕获 to_user_id + context_token）
/// 3. 发送（sendmessage）+ 图片上传（getuploadurl → AES-128-ECB 加密 → CDN POST → sendmessage）
///
/// 协议参考官方 npm 包 @tencent-weixin/openclaw-weixin（v2.4.6），请求头格式见 AddCommonHeaders。
/// </summary>
public static class WechatClawbotHelper
{
    internal const string ApiBase = "https://ilinkai.weixin.qq.com";
    internal const string CdnBase = "https://novac2c.cdn.weixin.qq.com/c2c";
    internal const string ChannelVersion = "1.0.2";
    internal const string BotAgent = "BetterGI/0.64.0";
    internal const string AppId = "bot";
    internal const string AppClientVersion = "65538"; // 0x00010002 = 1.0.2

    private const int VerifyCodeLength = 4;
    private const int LoginTimeoutSeconds = 300;
    private const int BindTimeoutSeconds = 90;
    private const int DefaultLongPollTimeoutMs = 35000;

    /// <summary>
    /// 扫码登录：获取二维码并轮询扫码状态，确认后返回 bot_token、base_url、用户 ID 等。
    ///
    /// 流程：POST get_bot_qrcode → GET get_qrcode_status（长轮询 ~30s/次）→ confirmed。
    /// 整体超时 300 秒。confirmed 时必须返回 ilink_user_id（扫码用户身份），否则拒绝继续。
    /// </summary>
    /// <param name="onQrCodeUrl">获取到二维码链接后的回调，用于 UI 展示/打开浏览器</param>
    /// <param name="cancellationToken">取消令牌（用户点击取消时触发）</param>
    public static async Task<WechatClawbotLoginResult> LoginAsync(
        Action<string> onQrCodeUrl,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(LoginTimeoutSeconds));
        var ct = timeoutCts.Token;

        var (qrcode, qrCodeUrl) = await GetBotQrCodeAsync(httpClient, ct);
        onQrCodeUrl(qrCodeUrl);

        while (!ct.IsCancellationRequested)
        {
            WechatClawbotQrStatus status;
            try
            {
                status = await GetQrCodeStatusAsync(httpClient, qrcode, ct);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 用户主动取消
                throw;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 内部登录超时（timeoutCts 触发），转成明确错误而不是“已取消”
                throw new NotifierException("登录超时，请重试");
            }
            catch (OperationCanceledException)
            {
                // 单次长轮询超时，视为 wait 继续；退避 1 秒再查，避免空转
                await Task.Delay(1000, CancellationToken.None);
                continue;
            }

            switch (status.Status)
            {
                case "confirmed":
                    if (string.IsNullOrWhiteSpace(status.BotToken))
                        throw new NotifierException("登录确认但未返回 bot_token");
                    // 身份校验依赖 ilink_user_id；缺失时拒绝继续，避免绑定到错误账号
                    if (string.IsNullOrWhiteSpace(status.UserId))
                        throw new NotifierException("登录确认但未返回用户 ID，无法完成身份校验，请重新扫码");
                    return new WechatClawbotLoginResult(
                        status.BotToken!,
                        status.BotId ?? string.Empty,
                        status.UserId!,
                        status.BaseUrl ?? ApiBase);
                case "expired":
                    throw new NotifierException("二维码已过期，请重新登录");
                case "need_verifycode":
                case "verify_code_blocked":
                    throw new NotifierException("登录需要手机验证码，当前版本暂不支持，请稍后重试");
                case "binded_redirect":
                    throw new NotifierException("该机器人已绑定过其他实例，请重新扫码");
                case "wait":
                case "scaned":
                case "scaned_but_redirect":
                default:
                    break;
            }

            try
            {
                await Task.Delay(1000, ct);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 用户取消
                throw;
            }
            catch (OperationCanceledException)
            {
                // 登录超时（timeoutCts 在轮询间隔内触发）→ 转明确错误
                throw new NotifierException("登录超时，请重试");
            }
        }

        throw new NotifierException("登录超时，请重试");
    }

    /// <summary>
    /// 一次性验证码绑定：长轮询 getupdates，等待扫码用户发送验证码，返回 to_user_id 和 context_token。
    /// 绑定窗口内仅接受 expectedUserId 发送的消息（安全校验），防止其他用户误绑。
    /// </summary>
    /// <param name="botToken">扫码登录获得的 bot_token</param>
    /// <param name="baseUrl">登录响应返回的 API 基础地址（为空回退默认主机）</param>
    /// <param name="expectedUserId">扫码登录返回的用户 ID（ilink_user_id）。必须非空，
    /// 仅接受该用户发送的验证码，防止绑定窗口内其他用户误发导致绑错账号。</param>
    /// <param name="onVerifyCode">生成验证码后的回调，用于 UI 提示用户发送该验证码</param>
    /// <param name="cancellationToken">取消令牌（用户点击取消时触发）</param>
    public static async Task<WechatClawbotBindResult> BindAsync(
        string botToken,
        string baseUrl,
        string expectedUserId,
        Action<string> onVerifyCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(botToken))
            throw new NotifierException("微信 Clawbot BotToken 为空");

        // 必须校验扫码用户身份，否则绑定窗口内其他用户可凭验证码绑定到错误账号
        if (string.IsNullOrWhiteSpace(expectedUserId))
            throw new NotifierException("登录未返回用户 ID，无法校验绑定身份，请重新扫码登录");

        var normalizeBaseUrl = NormalizeBaseUrl(baseUrl);
        var verifyCode = GenerateVerifyCode();
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(BindTimeoutSeconds));
        var ct = timeoutCts.Token;

        onVerifyCode(verifyCode);

        var buf = string.Empty;
        var timeoutMs = DefaultLongPollTimeoutMs;
        while (!ct.IsCancellationRequested)
        {
            WechatClawbotGetUpdatesResponse resp;
            try
            {
                resp = await GetUpdatesAsync(httpClient, normalizeBaseUrl, botToken, buf, timeoutMs, ct);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // 长轮询超时，继续
                continue;
            }

            if (resp.Ret != 0 || resp.ErrCode != 0)
                throw new NotifierException($"getupdates 失败：ret={resp.Ret} errcode={resp.ErrCode} errmsg={resp.ErrMsg}");

            if (!string.IsNullOrWhiteSpace(resp.GetUpdatesBuf))
                buf = resp.GetUpdatesBuf;
            if (resp.LongPollingTimeoutMs is > 0)
                timeoutMs = resp.LongPollingTimeoutMs.Value;

            foreach (var msg in resp.Msgs ?? [])
            {
                var text = ExtractText(msg);
                // 仅接受扫码用户发来的验证码消息，且必须携带 context_token，防止其他用户误绑
                var isExpectedSender = string.Equals(msg.FromUserId, expectedUserId, StringComparison.Ordinal);
                if (text != null && text.Contains(verifyCode) && isExpectedSender
                    && !string.IsNullOrWhiteSpace(msg.ContextToken))
                {
                    return new WechatClawbotBindResult(msg.FromUserId!, msg.ContextToken!, buf);
                }
            }
        }

        throw new NotifierException("绑定超时，请重试");
    }

    /// <summary>
    /// 构建所有 POST 请求体附带的 base_info 块（协议要求）。
    /// channel_version 为 iLink 协议版本；bot_agent 用于后台观测与流量归因。
    /// 该块随每个请求一起发送，服务端据此识别客户端类型与版本。
    /// </summary>
    internal static object BuildBaseInfo() => new
    {
        channel_version = ChannelVersion,
        bot_agent = BotAgent,
    };

    /// <summary>
    /// X-WECHAT-UIN 请求头：随机 uint32（大端序）→ 十进制字符串 → base64 编码。
    /// 每次请求随机生成，防止请求重放攻击（服务端以 UIN + 时间戳去重）。
    /// </summary>
    internal static string RandomWechatUin()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        var value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value.ToString()));
    }

    /// <summary>
    /// 生成唯一消息客户端 ID（openclaw-weixin- + 32 位随机 hex）。
    /// 用于 sendmessage 的 client_id 字段，服务端可借此区分不同发送方。
    /// </summary>
    internal static string GenerateClientId()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return "openclaw-weixin-" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// 规范化 API 基础地址：空值回退默认主机（ilinkai.weixin.qq.com），并去除尾部斜杠。
    /// 微信 ClawBot 协议的 host 可能因 IDC 重定向而变化，base_url 由登录响应提供。
    /// </summary>
    internal static string NormalizeBaseUrl(string? baseUrl)
    {
        var url = string.IsNullOrWhiteSpace(baseUrl) ? ApiBase : baseUrl.Trim();
        return url.TrimEnd('/');
    }

    /// <summary>
    /// 为 POST 请求添加完整请求头（Content-Type / AuthorizationType / X-WECHAT-UIN / iLink 标识 / Authorization）。
    /// </summary>
    internal static void AddCommonHeaders(HttpRequestMessage request, string? botToken)
    {
        request.Headers.TryAddWithoutValidation("AuthorizationType", "ilink_bot_token");
        request.Headers.TryAddWithoutValidation("X-WECHAT-UIN", RandomWechatUin());
        request.Headers.TryAddWithoutValidation("iLink-App-Id", AppId);
        request.Headers.TryAddWithoutValidation("iLink-App-ClientVersion", AppClientVersion);
        if (!string.IsNullOrWhiteSpace(botToken))
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {botToken}");
    }

    /// <summary>
    /// 为 GET 请求添加请求头。官方实现中 get_qrcode_status 只携带 iLink 标识头，
    /// 额外携带 X-WECHAT-UIN / AuthorizationType 会导致服务端立即返回 wait 且二维码快速过期。
    /// </summary>
    internal static void AddGetHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("iLink-App-Id", AppId);
        request.Headers.TryAddWithoutValidation("iLink-App-ClientVersion", AppClientVersion);
    }

    /// <summary>
    /// 长轮询 getupdates：服务端 hold 35s，客户端通过 linked CTS 按服务器建议超时取消。
    /// OperationCanceledException（非用户取消）视为正常轮询超时，调用方可安全 continue。
    /// 业务失败码 ret/errcode 非零视为协议错误；errcode -14 表示会话过期需重新登录。
    /// </summary>
    internal static async Task<WechatClawbotGetUpdatesResponse> GetUpdatesAsync(
        HttpClient httpClient,
        string baseUrl,
        string botToken,
        string getUpdatesBuf,
        int timeoutMs,
        CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            get_updates_buf = getUpdatesBuf,
            base_info = BuildBaseInfo(),
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{NormalizeBaseUrl(baseUrl)}/ilink/bot/getupdates")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        AddCommonHeaders(request, botToken);

        // 长轮询超时：服务端 hold 约 35s，客户端按服务端建议值取消本次请求，
        // 调用方将 OperationCanceledException（非用户取消）视为正常超时并继续轮询。
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);
        using var response = await httpClient.SendAsync(request, timeoutCts.Token);
        var text = await response.Content.ReadAsStringAsync(timeoutCts.Token);
        if (!response.IsSuccessStatusCode)
            throw new NotifierException($"getupdates HTTP {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<WechatClawbotGetUpdatesResponse>(text)
               ?? throw new NotifierException("getupdates 响应解析失败");
    }

    internal static string? ExtractText(WechatClawbotMessage msg)
    {
        if (msg.ItemList == null)
            return null;
        foreach (var item in msg.ItemList)
        {
            if (item.Type == 1 && item.TextItem?.Text != null)
                return item.TextItem.Text;
        }
        return null;
    }

    private static string GenerateVerifyCode()
    {
        Span<byte> bytes = stackalloc byte[VerifyCodeLength];
        RandomNumberGenerator.Fill(bytes);
        var code = new char[VerifyCodeLength];
        for (var i = 0; i < VerifyCodeLength; i++)
            code[i] = (char)('0' + bytes[i] % 10);
        return new string(code);
    }

    private static async Task<(string Qrcode, string QrCodeUrl)> GetBotQrCodeAsync(HttpClient httpClient, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { local_token_list = Array.Empty<string>() });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/ilink/bot/get_bot_qrcode?bot_type=3")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        AddCommonHeaders(request, null);
        using var response = await httpClient.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new NotifierException($"获取登录二维码失败 HTTP {(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var qrcode = root.TryGetProperty("qrcode", out var q) ? q.GetString() : null;
        if (string.IsNullOrWhiteSpace(qrcode))
            throw new NotifierException("获取登录二维码失败：响应缺少 qrcode");
        var url = root.TryGetProperty("qrcode_img_content", out var u) ? u.GetString() : null;
        if (string.IsNullOrWhiteSpace(url))
            url = $"https://liteapp.weixin.qq.com/q/{qrcode}?bot_type=3";
        return (qrcode!, url!);
    }

    private static async Task<WechatClawbotQrStatus> GetQrCodeStatusAsync(HttpClient httpClient, string qrcode, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/ilink/bot/get_qrcode_status?qrcode={Uri.EscapeDataString(qrcode)}");
        AddGetHeaders(request);
        using var response = await httpClient.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new NotifierException($"查询扫码状态失败 HTTP {(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        return new WechatClawbotQrStatus(
            root.TryGetProperty("status", out var s) ? s.GetString() ?? "wait" : "wait",
            root.TryGetProperty("bot_token", out var bt) ? bt.GetString() : null,
            root.TryGetProperty("ilink_bot_id", out var bid) ? bid.GetString() : null,
            root.TryGetProperty("ilink_user_id", out var uid) ? uid.GetString() : null,
            root.TryGetProperty("baseurl", out var bu) ? bu.GetString() : null,
            root.TryGetProperty("redirect_host", out var rh) ? rh.GetString() : null);
    }
}

public sealed class WechatClawbotLoginResult
{
    public string BotToken { get; }
    public string BotId { get; }
    public string UserId { get; }
    public string BaseUrl { get; }

    public WechatClawbotLoginResult(string botToken, string botId, string userId, string baseUrl)
    {
        BotToken = botToken;
        BotId = botId;
        UserId = userId;
        BaseUrl = baseUrl;
    }
}

public sealed class WechatClawbotBindResult
{
    public string ToUserId { get; }
    public string ContextToken { get; }
    public string GetUpdatesBuf { get; }

    public WechatClawbotBindResult(string toUserId, string contextToken, string getUpdatesBuf)
    {
        ToUserId = toUserId;
        ContextToken = contextToken;
        GetUpdatesBuf = getUpdatesBuf;
    }
}

internal sealed record WechatClawbotQrStatus(
    string Status,
    string? BotToken,
    string? BotId,
    string? UserId,
    string? BaseUrl,
    string? RedirectHost);

internal sealed class WechatClawbotGetUpdatesResponse
{
    [JsonPropertyName("ret")] public int Ret { get; set; }
    [JsonPropertyName("errcode")] public int ErrCode { get; set; }
    [JsonPropertyName("errmsg")] public string? ErrMsg { get; set; }
    [JsonPropertyName("msgs")] public List<WechatClawbotMessage>? Msgs { get; set; }
    [JsonPropertyName("get_updates_buf")] public string? GetUpdatesBuf { get; set; }
    [JsonPropertyName("longpolling_timeout_ms")] public int? LongPollingTimeoutMs { get; set; }
}

internal sealed class WechatClawbotMessage
{
    [JsonPropertyName("from_user_id")] public string? FromUserId { get; set; }
    [JsonPropertyName("to_user_id")] public string? ToUserId { get; set; }
    [JsonPropertyName("context_token")] public string? ContextToken { get; set; }
    [JsonPropertyName("item_list")] public List<WechatClawbotMessageItem>? ItemList { get; set; }
}

internal sealed class WechatClawbotMessageItem
{
    [JsonPropertyName("type")] public int Type { get; set; }
    [JsonPropertyName("text_item")] public WechatClawbotTextItem? TextItem { get; set; }
}

internal sealed class WechatClawbotTextItem
{
    [JsonPropertyName("text")] public string? Text { get; set; }
}
