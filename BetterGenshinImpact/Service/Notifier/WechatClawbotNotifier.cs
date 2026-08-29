using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using BetterGenshinImpact.Service.Notifier.Exception;
using BetterGenshinImpact.Service.Notifier.Interface;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BetterGenshinImpact.Service.Notifier;

/// <summary>
/// 微信 Clawbot 通知器。
/// 通过微信 iLink 协议（ilinkai.weixin.qq.com）将 BetterGI 事件推送到用户微信。
/// 支持文本消息和截图图片消息（CDN AES-128-ECB 加密上传）。
/// 内置实现，不依赖 OpenClaw；扫码登录后由后台长轮询维持 context_token。
/// </summary>
public sealed class WechatClawbotNotifier : INotifier, IDisposable
{
    private static readonly ILogger<WechatClawbotNotifier> Logger = App.GetLogger<WechatClawbotNotifier>();

    public string Name { get; } = "微信 Clawbot";

    private const int MaxRetry = 3;

    private readonly HttpClient _httpClient;
    private readonly string _botToken;
    private readonly string _toUserId;
    private readonly string _baseUrl;
    private readonly WechatClawbotSession _session;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// 构造微信通知器，固定构造时的凭证快照（bot_token、user_id、base_url），
    /// 发送期间即使配置被重新绑定变更，旧实例也不会混用新旧凭证。
    /// </summary>
    /// <param name="httpClient">共享的 HttpClient 实例（30s 超时）</param>
    /// <param name="config">通知配置（读取绑定凭证，运行期不再读取）</param>
    /// <param name="startSession">仅主实例启动长轮询会话；子实例（桌面分身等）保留发送能力但不推游标。</param>
    public WechatClawbotNotifier(HttpClient httpClient, NotificationConfig config, bool startSession = true)
    {
        _httpClient = httpClient;
        _botToken = config.WechatClawbotBotToken;
        _toUserId = config.WechatClawbotToUserId;
        _baseUrl = WechatClawbotHelper.NormalizeBaseUrl(config.WechatClawbotBaseUrl);
        _session = new WechatClawbotSession(_botToken, _baseUrl, _toUserId);
        if (startSession)
            _ = _session.StartAsync();
    }

    /// <summary>
    /// 发送通知：先发文本，再发截图（如有）。
    /// 截图上传链路：JPEG 编码 → getuploadurl 获取预签名 → AES-128-ECB 加密 → CDN POST 上传 → sendmessage。
    /// 图片发送失败时自动降级为纯文本（文本已成功），不阻断主流程。
    /// </summary>
    public async Task SendAsync(BaseNotificationData content)
    {
        if (string.IsNullOrWhiteSpace(_botToken))
            throw new NotifierException("微信 Clawbot BotToken 为空，请先扫码登录");

        if (string.IsNullOrWhiteSpace(_toUserId))
            throw new NotifierException("微信 Clawbot 未绑定用户，请先完成绑定");

        var ct = _cts.Token;
        try
        {
            var text = GenerateMessage(content);
            await SendTextAsync(text, ct);

            if (content.Screenshot != null)
            {
                try
                {
                    await SendImageAsync(content.Screenshot, ct);
                }
                catch (System.Exception ex)
                {
                    // 图片发送失败时降级为纯文本，不阻断通知
                    Logger.LogWarning("微信 Clawbot 图片发送失败，降级为纯文本: {Ex}", ex.Message);
                }
            }
        }
        catch (NotifierException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            throw new NotifierException($"发送微信 Clawbot 消息失败: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _session.Dispose();
        _cts.Dispose();
    }

    /// <summary>
    /// 生成通知文本，包含结果标记（成功/失败/警告）和时间戳。
    /// </summary>
    private static string GenerateMessage(BaseNotificationData data)
    {
        var sb = new StringBuilder();
        var mark = data.Result switch
        {
            NotificationEventResult.Success => "\u2705",
            NotificationEventResult.Fail => "\u274C",
            _ => "\u26A0\uFE0F"
        };
        sb.Append($"[BetterGI] {mark} ");
        if (!string.IsNullOrWhiteSpace(data.Message))
            sb.Append(data.Message);
        sb.AppendLine();
        sb.Append($"\uD83D\uDD50 {data.Timestamp:yyyy-MM-dd HH:mm:ss}");
        return sb.ToString();
    }

    /// <summary>
    /// 发送纯文本消息。注意：此请求非幂等，不重试，避免重复消息。
    /// </summary>
    private async Task SendTextAsync(string text, CancellationToken ct)
    {
        var contextToken = await _session.GetContextTokenAsync(ct);
        var body = JsonSerializer.Serialize(new
        {
            msg = new
            {
                from_user_id = string.Empty,
                to_user_id = _toUserId,
                client_id = WechatClawbotHelper.GenerateClientId(),
                message_type = 2,
                message_state = 2,
                context_token = contextToken,
                item_list = new[]
                {
                    new { type = 1, text_item = new { text } }
                }
            },
            base_info = WechatClawbotHelper.BuildBaseInfo(),
        });
        await SendMessageAsync(body, ct);
    }

    /// <summary>
    /// 发送截图图片消息：先上传图片拿到 CDN 引用，再发图片消息。
    /// </summary>
    private async Task SendImageAsync(Image<Rgb24> screenshot, CancellationToken ct)
    {
        byte[] imageBytes;
        using (var ms = new MemoryStream())
        {
            screenshot.SaveAsJpeg(ms);
            imageBytes = ms.ToArray();
        }

        var uploaded = await UploadImageAsync(imageBytes, ct);
        var contextToken = await _session.GetContextTokenAsync(ct);

        var body = JsonSerializer.Serialize(new
        {
            msg = new
            {
                from_user_id = string.Empty,
                to_user_id = _toUserId,
                client_id = WechatClawbotHelper.GenerateClientId(),
                message_type = 2,
                message_state = 2,
                context_token = contextToken,
                item_list = new[]
                {
                    new
                    {
                        type = 2,
                        image_item = new
                        {
                            media = new
                            {
                                encrypt_query_param = uploaded.DownloadParam,
                                // 注意：aes_key 是对 hex 字符串（ASCII 文本）做 base64，而非对原始密钥字节。
                                // 官方实现 UploadedFileInfo.aeskey 为 hex 字符串，发送时
                                // Buffer.from(hexString).toString("base64")；接收端按
                                // base64解码 → ASCII → hex解码 还原 16 字节密钥。
                                aes_key = Convert.ToBase64String(Encoding.UTF8.GetBytes(uploaded.AesKeyHex)),
                                encrypt_type = 1,
                            },
                            mid_size = uploaded.CiphertextSize,
                        }
                    }
                }
            },
            base_info = WechatClawbotHelper.BuildBaseInfo(),
        });
        await SendMessageAsync(body, ct);
    }

    /// <summary>
    /// 调用 sendmessage 接口。非幂等，不重试；读取响应体检查业务返回码。
    /// </summary>
    private async Task SendMessageAsync(string body, CancellationToken ct)
    {
        var baseUrl = _baseUrl;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/ilink/bot/sendmessage")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        WechatClawbotHelper.AddCommonHeaders(request, _botToken);
        using var response = await _httpClient.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new NotifierException($"sendmessage HTTP {(int)response.StatusCode}: {text}");

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var ret = root.TryGetProperty("ret", out var retElement) ? retElement.GetInt32() : 0;
        var errcode = root.TryGetProperty("errcode", out var errcodeElement) ? errcodeElement.GetInt32() : 0;
        // 与 getupdates 一致，ret / errcode 任一非零均视为业务失败
        if (ret != 0 || errcode != 0)
        {
            var errmsg = root.TryGetProperty("errmsg", out var errElement) ? errElement.GetString() : null;
            throw new NotifierException($"sendmessage 返回错误 ret={ret} errcode={errcode} errmsg={errmsg}");
        }
    }

    /// <summary>
    /// 上传图片到微信 CDN（幂等操作，全链路重试）：
    /// 1. 计算明文 MD5、PKCS7 填充后密文大小、随机 filekey/aeskey
    /// 2. POST getuploadurl → 获取 upload_full_url 或 upload_param
    /// 3. AES-128-ECB 加密明文 → POST 密文到 CDN → 获取 x-encrypted-param
    /// 4. 返回 downloadParam 供 sendmessage 的 image_item.media 引用
    /// </summary>
    private async Task<WechatClawbotUploadedImage> UploadImageAsync(byte[] imageBytes, CancellationToken ct)
    {
        var rawsize = imageBytes.Length;
        var rawfilemd5 = Convert.ToHexString(MD5.HashData(imageBytes)).ToLowerInvariant();
        var filesize = AesEcbPaddedSize(rawsize);
        var filekey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var aeskey = RandomNumberGenerator.GetBytes(16);
        var aeskeyHex = Convert.ToHexString(aeskey).ToLowerInvariant();

        var uploadUrl = await WithRetryAsync(() => GetUploadUrlAsync(filekey, rawsize, rawfilemd5, filesize, aeskeyHex, ct), ct);
        var downloadParam = await WithRetryAsync(() => UploadToCdnAsync(uploadUrl, filekey, imageBytes, aeskey, ct), ct);

        return new WechatClawbotUploadedImage(downloadParam, aeskeyHex, filesize);
    }

    /// <summary>
    /// 调用 getuploadurl 接口获取 CDN 上传地址。
    /// </summary>
    private async Task<WechatClawbotUploadUrl> GetUploadUrlAsync(
        string filekey, int rawsize, string rawfilemd5, int filesize, string aeskeyHex, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            filekey,
            media_type = 1,
            to_user_id = _toUserId,
            rawsize,
            rawfilemd5,
            filesize,
            no_need_thumb = true,
            aeskey = aeskeyHex,
            base_info = WechatClawbotHelper.BuildBaseInfo(),
        });
        var baseUrl = _baseUrl;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/ilink/bot/getuploadurl")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        WechatClawbotHelper.AddCommonHeaders(request, _botToken);
        using var response = await _httpClient.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new WechatClawbotHttpException((int)response.StatusCode, $"getuploadurl HTTP {(int)response.StatusCode}: {text}");

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var uploadParam = root.TryGetProperty("upload_param", out var up) ? up.GetString() : null;
        var uploadFullUrl = root.TryGetProperty("upload_full_url", out var ufu) ? ufu.GetString() : null;
        if (string.IsNullOrWhiteSpace(uploadParam) && string.IsNullOrWhiteSpace(uploadFullUrl))
            throw new NotifierException("getuploadurl 未返回上传地址");
        return new WechatClawbotUploadUrl(uploadParam, uploadFullUrl);
    }

    /// <summary>
    /// 将图片 AES-128-ECB 加密后 POST 到 CDN，返回下载加密参数（x-encrypted-param）。
    /// </summary>
    private async Task<string> UploadToCdnAsync(
        WechatClawbotUploadUrl uploadUrl, string filekey, byte[] imageBytes, byte[] aeskey, CancellationToken ct)
    {
        var ciphertext = AesEcbEncrypt(imageBytes, aeskey);
        var cdnUrl = !string.IsNullOrWhiteSpace(uploadUrl.UploadFullUrl)
            ? uploadUrl.UploadFullUrl!
            : $"{WechatClawbotHelper.CdnBase}/upload?encrypted_query_param={Uri.EscapeDataString(uploadUrl.UploadParam!)}&filekey={Uri.EscapeDataString(filekey)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, cdnUrl)
        {
            Content = new ByteArrayContent(ciphertext),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errText = await response.Content.ReadAsStringAsync(ct);
            throw new WechatClawbotHttpException((int)response.StatusCode, $"CDN 上传失败 HTTP {(int)response.StatusCode}: {errText}");
        }

        var downloadParam = response.Headers.TryGetValues("x-encrypted-param", out var values)
            ? string.Join(string.Empty, values)
            : null;
        if (string.IsNullOrWhiteSpace(downloadParam))
            throw new NotifierException("CDN 上传响应缺少 x-encrypted-param");
        return downloadParam!;
    }

    /// <summary>
    /// AES-128-ECB 加密（PKCS7 填充）。
    /// </summary>
    private static byte[] AesEcbEncrypt(byte[] plaintext, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
    }

    /// <summary>
    /// 计算 AES-128-ECB（PKCS7）加密后的密文大小。
    /// </summary>
    private static int AesEcbPaddedSize(int plaintextSize) => ((plaintextSize + 16) / 16) * 16;

    /// <summary>
    /// 判断异常是否可重试。分类策略：
    /// - WechatClawbotHttpException：仅 5xx/429/400 可重试（401/403/404 客户端错误不重试）
    /// - HttpRequestException（无状态码）：网络故障可重试
    /// - OperationCanceledException：用户取消不可重试（超时已在调用方处理）
    /// - JsonException/InvalidOperationException：数据错误不重试
    /// - NotifierException：业务错误不重试
    /// 注意：仅用于幂等操作（getuploadurl、CDN 上传），非幂等的 sendmessage 不走此路径。
    /// </summary>
    private static bool IsRetryable(System.Exception ex)
    {
        if (ex is WechatClawbotHttpException whe)
            return (whe.StatusCode >= 500 && whe.StatusCode <= 599) || whe.StatusCode == 429 || whe.StatusCode == 400;
        if (ex is HttpRequestException hre)
        {
            var statusCode = hre.StatusCode;
            if (statusCode.HasValue)
            {
                var code = (int)statusCode.Value;
                return (code >= 500 && code <= 599) || code == 429 || code == 400;
            }
            return true;
        }
        if (ex is TaskCanceledException || ex is OperationCanceledException)
            return false;
        if (ex is JsonException || ex is InvalidOperationException)
            return false;
        // NotifierException 为业务错误（如 getuploadurl 缺少上传地址、CDN 响应缺少 x-encrypted-param），
        // 属于确定性失败，不应重试，避免重复上传截图并累计退避延迟。
        if (ex is NotifierException)
            return false;
        return true;
    }

    /// <summary>
    /// 带指数退避的重试执行（有返回值版本）。
    /// </summary>
    private async Task<T> WithRetryAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        System.Exception? lastException = null;
        for (var attempt = 0; attempt <= MaxRetry; attempt++)
        {
            try
            {
                return await action();
            }
            catch (System.Exception ex) when (attempt < MaxRetry && IsRetryable(ex))
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(1500 * (1 << attempt)), ct);
            }
        }
        throw lastException ?? new NotifierException("微信 Clawbot 请求重试后仍失败");
    }

    private sealed record WechatClawbotUploadUrl(string? UploadParam, string? UploadFullUrl);

    private sealed record WechatClawbotUploadedImage(string DownloadParam, string AesKeyHex, int CiphertextSize);

    /// <summary>
    /// 携带 HTTP 状态码的通知异常，供重试判定使用（避免把 401/403/404 等客户端错误当成可重试）。
    /// </summary>
    private sealed class WechatClawbotHttpException(int statusCode, string message) : NotifierException(message)
    {
        public int StatusCode { get; } = statusCode;
    }
}
