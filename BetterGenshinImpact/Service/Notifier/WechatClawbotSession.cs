using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.Notifier;

/// <summary>
/// 微信 Clawbot 会话维持器。
/// 后台长轮询 getupdates，持续刷新 context_token（会过期）和 get_updates_buf（同步游标），
/// 并回写 NotificationConfig 以便重启/重建后继续。这两个字段已被 AllConfig 排除，
/// 更新不会触发通知器刷新，因此不会自我销毁。
/// 类似 QQ 渠道的 WebSocket 心跳，但 iLink 协议用 HTTP 长轮询实现。
/// </summary>
public sealed class WechatClawbotSession : IDisposable
{
    private static readonly ILogger<WechatClawbotSession> Logger = App.GetLogger<WechatClawbotSession>();

    private const int DefaultLongPollTimeoutMs = 35000;
    private const int MaxConsecutiveFailures = 3;
    private const int BackoffDelayMs = 30000;
    private const int RetryDelayMs = 2000;

    private readonly string _botToken;
    private readonly string _baseUrl;
    private readonly NotificationConfig _config;
    private readonly HttpClient _httpClient;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);

    private string _contextToken;
    private string _getUpdatesBuf;
    private Task? _loopTask;
    private bool _disposed;

    public WechatClawbotSession(NotificationConfig config)
    {
        _config = config;
        _botToken = config.WechatClawbotBotToken;
        _baseUrl = WechatClawbotHelper.NormalizeBaseUrl(config.WechatClawbotBaseUrl);
        _contextToken = config.WechatClawbotContextToken;
        _getUpdatesBuf = config.WechatClawbotGetUpdatesBuf;
        // 长轮询专用 HttpClient：超时需大于服务端 hold 时间（35s），不能复用 30s 的共享客户端
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    /// <summary>
    /// 启动后台长轮询循环（幂等，重复调用不会重复启动）。
    /// </summary>
    public void Start()
    {
        if (_loopTask != null)
            return;
        if (string.IsNullOrWhiteSpace(_botToken) || string.IsNullOrWhiteSpace(_config.WechatClawbotToUserId))
            return;
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    /// <summary>
    /// 获取当前缓存的 context_token（线程安全）。
    /// </summary>
    public async Task<string> GetContextTokenAsync(CancellationToken ct)
    {
        await _tokenSemaphore.WaitAsync(ct);
        try
        {
            return _contextToken;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        _tokenSemaphore.Dispose();
        _httpClient.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var timeoutMs = DefaultLongPollTimeoutMs;
        var consecutiveFailures = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var resp = await WechatClawbotHelper.GetUpdatesAsync(
                    _httpClient, _baseUrl, _botToken, _getUpdatesBuf, timeoutMs, ct);

                if (resp.Ret != 0 || resp.ErrCode != 0)
                {
                    consecutiveFailures++;
                    Logger.LogWarning("微信 Clawbot getupdates 失败：ret={Ret} errcode={ErrCode} errmsg={ErrMsg}（{Count}/{Max}）",
                        resp.Ret, resp.ErrCode, resp.ErrMsg, consecutiveFailures, MaxConsecutiveFailures);
                    if (consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        consecutiveFailures = 0;
                        await DelayAsync(BackoffDelayMs, ct);
                    }
                    else
                    {
                        await DelayAsync(RetryDelayMs, ct);
                    }
                    continue;
                }

                consecutiveFailures = 0;
                // 回写游标到配置（AllConfig 已排除该字段，不会触发通知器刷新）
                if (!string.IsNullOrWhiteSpace(resp.GetUpdatesBuf) && resp.GetUpdatesBuf != _getUpdatesBuf)
                {
                    _getUpdatesBuf = resp.GetUpdatesBuf;
                    _config.WechatClawbotGetUpdatesBuf = resp.GetUpdatesBuf;
                }

                if (resp.LongPollingTimeoutMs is > 0)
                    timeoutMs = resp.LongPollingTimeoutMs.Value;

                foreach (var msg in resp.Msgs ?? [])
                {
                    if (string.Equals(msg.FromUserId, _config.WechatClawbotToUserId, StringComparison.Ordinal) &&
                        !string.IsNullOrWhiteSpace(msg.ContextToken))
                    {
                        // 回写 context_token（AllConfig 已排除，不会触发通知器刷新）
                        await _tokenSemaphore.WaitAsync(ct);
                        try
                        {
                            _contextToken = msg.ContextToken!;
                        }
                        finally
                        {
                            _tokenSemaphore.Release();
                        }

                        _config.WechatClawbotContextToken = msg.ContextToken!;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                // 长轮询超时，继续
            }
            catch (System.Exception ex)
            {
                consecutiveFailures++;
                Logger.LogWarning("微信 Clawbot 长轮询异常（{Count}/{Max}）：{Ex}",
                    consecutiveFailures, MaxConsecutiveFailures, ex.Message);
                if (consecutiveFailures >= MaxConsecutiveFailures)
                {
                    consecutiveFailures = 0;
                    await DelayAsync(BackoffDelayMs, ct);
                }
                else
                {
                    await DelayAsync(RetryDelayMs, ct);
                }
            }
        }
    }

    private static async Task DelayAsync(int ms, CancellationToken ct)
    {
        try
        {
            await Task.Delay(ms, ct);
        }
        catch (OperationCanceledException)
        {
            // 正常关闭
        }
    }
}
