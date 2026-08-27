using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.Notifier;

/// <summary>
/// 微信 Clawbot 会话维持器。
///
/// 生命周期：构造 → StartAsync() → InitAsync（从 Store 恢复状态）+ RunLoopAsync（长轮询循环）→ Dispose。
/// - StartAsync 仅恢复上次会话状态（快速完成），不阻塞发送。
/// - RunLoopAsync 持续轮询 getupdates，刷新 context_token/get_updates_buf，
///   并通过 WechatClawbotSessionStore 独立持久化，不写入 NotificationConfig，
///   避免触发 AllConfig 的 PropertyChanged → Save() / RefreshNotifiers() 副作用。
/// - Dispose 依次取消令牌、等待 _initTask 和 _loopTask 完整退出、再释放依赖资源，
///   避免旧循环仍访问已释放的信号量/HttpClient 造成 ObjectDisposedException。
///
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
    /// 获取当前缓存的 context_token（线程安全，SemaphoreSlim 保护）。
    /// 会等待 _initTask 完成（从 Store 恢复状态），确保不返回空令牌。
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
    /// 停止会话并释放所有资源。等待顺序：
    /// 1. 取消 CTS（通知长轮询停止）
    /// 2. 等待 _initTask（状态恢复）完成
    /// 3. 等待 _loopTask（长轮询循环）完成
    /// 4. 释放 CTS / SemaphoreSlim / HttpClient
    /// 严格按此顺序执行，防止循环在资源释放后仍访问它们。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cts.Cancel();

        // 先等初始化任务完成（它负责启动 _loopTask，但不等 _loopTask 结束）
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
            Logger.LogWarning("微信 Clawbot 会话初始化停止异常: {Ex}", ex.Message);
        }

        // 再等长轮询循环完整退出，避免其仍访问已释放的信号量/HttpClient
        try
        {
            _loopTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // 取消导致的退出是预期行为
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning("微信 Clawbot 长轮询停止异常: {Ex}", ex.Message);
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
                // 长轮询客户端超时（>35s），视为正常事件间空闲，直接继续下一轮
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
