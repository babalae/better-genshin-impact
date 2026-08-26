using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.Notifier;

/// <summary>
/// 微信 Clawbot 会话维持器。
/// 后台长轮询 getupdates，持续刷新 context_token（会过期）和 get_updates_buf（同步游标）。
/// 轮询状态通过 WechatClawbotSessionStore 独立持久化（User/WechatClawbot/），
/// 不写入 NotificationConfig，避免触发全局配置保存与通知器刷新。
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
    private readonly string _toUserId;
    private readonly HttpClient _httpClient;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);

    private string _contextToken;
    private string _getUpdatesBuf;
    private Task? _initTask;
    private Task? _loopTask;
    private bool _disposed;

    public WechatClawbotSession(string botToken, string baseUrl, string toUserId)
    {
        _botToken = botToken;
        _baseUrl = WechatClawbotHelper.NormalizeBaseUrl(baseUrl);
        _toUserId = toUserId;
        _contextToken = string.Empty;
        _getUpdatesBuf = string.Empty;
        // 长轮询专用 HttpClient：超时需大于服务端 hold 时间（35s），不能复用 30s 的共享客户端
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    /// <summary>
    /// 异步启动：恢复上次会话状态后启动后台长轮询循环（幂等）。
    /// 返回的初始化任务仅负责恢复状态，在获取 context_token 前会被等待。
    /// </summary>
    public Task StartAsync()
    {
        if (_initTask != null)
            return _initTask;
        if (string.IsNullOrWhiteSpace(_botToken) || string.IsNullOrWhiteSpace(_toUserId))
            return Task.CompletedTask;

        _initTask = InitAsync(_cts.Token);
        return _initTask;
    }

    private async Task InitAsync(CancellationToken ct)
    {
        // 从独立存储恢复上次会话状态
        var (token, buf) = await WechatClawbotSessionStore.LoadAsync(_botToken);
        if (ct.IsCancellationRequested)
            return;
        _contextToken = token;
        _getUpdatesBuf = buf;

        _loopTask = Task.Run(() => RunLoopAsync(ct));
    }

    /// <summary>
    /// 获取当前缓存的 context_token（线程安全）。在会话初始化完成前会等待。
    /// </summary>
    public async Task<string> GetContextTokenAsync(CancellationToken ct)
    {
        if (_initTask != null && !_initTask.IsCompleted)
        {
            try
            {
                await _initTask.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (System.Exception)
            {
                // 初始化失败时继续用空令牌，交由上层报错
            }
        }

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

    /// <summary>
    /// 停止会话。取消令牌，并等待初始化与长轮询任务退出后再释放依赖资源，
    /// 避免旧循环仍在访问已释放的信号量/HttpClient。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cts.Cancel();
        try
        {
            _initTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // 取消导致的退出是预期行为
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning("微信 Clawbot 会话停止异常: {Ex}", ex.Message);
        }

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
                if (!string.IsNullOrWhiteSpace(resp.GetUpdatesBuf))
                    _getUpdatesBuf = resp.GetUpdatesBuf;

                if (resp.LongPollingTimeoutMs is > 0)
                    timeoutMs = resp.LongPollingTimeoutMs.Value;

                var tokenDirty = false;
                foreach (var msg in resp.Msgs ?? [])
                {
                    if (string.Equals(msg.FromUserId, _toUserId, StringComparison.Ordinal) &&
                        !string.IsNullOrWhiteSpace(msg.ContextToken))
                    {
                        await _tokenSemaphore.WaitAsync(ct);
                        try
                        {
                            if (_contextToken != msg.ContextToken)
                            {
                                _contextToken = msg.ContextToken!;
                                tokenDirty = true;
                            }
                        }
                        finally
                        {
                            _tokenSemaphore.Release();
                        }
                    }
                }

                // 独立持久化最新会话状态（不触发全局配置变更）
                if (tokenDirty || !string.IsNullOrWhiteSpace(resp.GetUpdatesBuf))
                    await WechatClawbotSessionStore.SaveAsync(_botToken, _contextToken, _getUpdatesBuf);
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
