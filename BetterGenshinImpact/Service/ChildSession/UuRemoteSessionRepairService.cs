using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.ChildSession;

/// <summary>
/// 检测并修复 UU 串流进程的主会话冲突，每个桌面分身启动周期最多尝试一次。
/// </summary>
public sealed class UuRemoteSessionRepairService
{
    /// <summary>停止旧进程后等待 UU 服务重新拉起串流进程的最长时间。</summary>
    private static readonly TimeSpan StreamingProcessRestartTimeout = TimeSpan.FromSeconds(20);

    /// <summary>等待异步会话断开完成的最长时间。</summary>
    private static readonly TimeSpan SessionDisconnectTimeout = TimeSpan.FromSeconds(5);

    /// <summary>会话断开后让 UU 服务稳定下来，再停止仍驻留的串流进程。</summary>
    private static readonly TimeSpan SessionDisconnectSettleDelay = TimeSpan.FromSeconds(1);

    /// <summary>短期轮询间隔。</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>覆盖 UU 在 RDP 登录后才启动或切换会话的观察窗口。</summary>
    private static readonly TimeSpan StartupMonitorDuration = TimeSpan.FromSeconds(60);

    /// <summary>持久化的功能开关与风险确认状态。</summary>
    private readonly ChildSessionConfig _config;

    /// <summary>Windows 平台操作边界。</summary>
    private readonly IUuRemoteSessionPlatform _platform;

    /// <summary>用于可测试的单调计时与延迟。</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>记录安全检查、操作结果和失败诊断。</summary>
    private readonly ILogger<UuRemoteSessionRepairService> _logger;

    /// <summary>串行化启动前检查与登录后检查。</summary>
    private readonly SemaphoreSlim _repairGate = new(1, 1);

    /// <summary>供 UI 读取的后台修复运行状态。</summary>
    private volatile bool _isRepairing;

    /// <summary>当前启动周期是否已经消耗过一次修复机会。</summary>
    private int _startupRepairAttemptCount;

    /// <summary>通过应用服务构造真实的 Windows 修复服务。</summary>
    public UuRemoteSessionRepairService(
        InstanceService instanceService,
        IConfigService configService,
        TimeProvider timeProvider,
        ILogger<UuRemoteSessionRepairService> logger)
        : this(configService.Get().ChildSessionConfig,
            new UuRemoteSessionPlatform(instanceService.Context, logger), timeProvider, logger)
    {
    }

    /// <summary>测试入口，不触碰真实 Windows 会话与 UU 进程。</summary>
    internal UuRemoteSessionRepairService(
        ChildSessionConfig config,
        IUuRemoteSessionPlatform platform,
        TimeProvider timeProvider,
        ILogger<UuRemoteSessionRepairService> logger)
    {
        _config = config;
        _platform = platform;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>报告检查、修复和启动后冲突；修复前等待 UI 完成风险提示呈现。</summary>
    public event EventHandler<UuRemoteRepairStateChangedEventArgs>? RepairStateChanged;

    /// <summary>用户是否启用了自动修复。</summary>
    public bool IsEnabled => _config.AutoRepairUuRemoteSession;

    /// <summary>当前是否正在执行启动前检查或修复。</summary>
    public bool IsRepairing => _isRepairing;

    /// <summary>仅允许控制台中的管理员根实例进行修复。</summary>
    public bool IsAvailable => _platform.IsRoot && _platform.IsElevated
                               && _platform.CurrentSessionId == _platform.ConsoleSessionId;

    /// <summary>用户是否确认过首次启用的风险说明。</summary>
    internal bool IsRiskAcknowledged => _config.UuRemoteRepairRiskAcknowledged;

    /// <summary>沿用现有配置自动保存机制修改开关。</summary>
    internal void SetEnabled(bool enabled) => _config.AutoRepairUuRemoteSession = enabled;

    /// <summary>记录用户对首次启用风险说明的确认。</summary>
    internal void AcknowledgeRisk() => _config.UuRemoteRepairRiskAcknowledged = true;

    /// <summary>调用方必须先取消并等待上一周期退出，再重置本周期的修复机会。</summary>
    internal void BeginStartupCycle() => Interlocked.Exchange(ref _startupRepairAttemptCount, 0);

    /// <summary>后台读取会话与进程状态，不执行断开或进程停止操作。</summary>
    internal Task<UuRemoteSessionSnapshot> InspectAsync(CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            var snapshot = _platform.CaptureSnapshot();
            cancellationToken.ThrowIfCancellationRequested();
            return snapshot;
        }, cancellationToken);

    /// <summary>后台执行检查与一次性修复，失败由调用方提示后继续桌面分身启动。</summary>
    internal Task<UuRemoteSessionRepairResult> TryRepairBeforeStartAsync(CancellationToken cancellationToken)
        => Task.Run(() => TryRepairCoreAsync(cancellationToken), cancellationToken);

    /// <summary>串行执行一次检查，在风险提示完成后才允许断开会话和停止串流进程。</summary>
    private async Task<UuRemoteSessionRepairResult> TryRepairCoreAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return UuRemoteSessionRepairResult.NotNeeded();
        }

        await _repairGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsEnabled)
            {
                return UuRemoteSessionRepairResult.NotNeeded();
            }

            _isRepairing = true;
            RaiseState(UuRemoteRepairState.Checking, "正在检查 UU 远程会话状态…");
            var snapshot = await InspectAsync(cancellationToken).ConfigureAwait(false);
            var assessment = UuRemoteSessionEvaluator.Evaluate(snapshot);
            _logger.LogDebug(
                "UU 会话检查：当前 Session {CurrentSession}，控制台 Session {ConsoleSession}，"
                + "GameViewerServer Session [{GameViewerSessions}]，冲突：{HasConflict}。会话详情：{Sessions}。",
                snapshot.CurrentSessionId, snapshot.ConsoleSessionId,
                string.Join(", ", assessment.GameViewerSessions), assessment.HasConflict, FormatSessions(snapshot.Sessions));
            if (!assessment.HasConflict)
            {
                RaiseState(UuRemoteRepairState.Idle, string.Empty);
                return UuRemoteSessionRepairResult.NotNeeded(assessment.BlockingReason);
            }

            if (Interlocked.CompareExchange(ref _startupRepairAttemptCount, 1, 0) != 0)
            {
                var message = "桌面分身启动后检测到 UU 串流进程仍位于非主桌面会话。"
                              + "本次启动已尝试过修复，不会再次自动修复，请重新连接 UU 或在下次启动桌面分身时处理。";
                _logger.LogWarning("{Message} GameViewerServer Session：[{Sessions}]。",
                    message, string.Join(", ", assessment.GameViewerSessions));
                RaiseState(UuRemoteRepairState.PostStartConflict, message);
                return UuRemoteSessionRepairResult.NotNeeded();
            }

            var blockingReason = GetSafetyBlockingReason(snapshot, assessment, snapshot.ConsoleSessionId);
            if (blockingReason is not null)
            {
                return ReportFailure(assessment.GameViewerSessions, blockingReason);
            }

            var warning = "检测到 UU 远程串流进程位于非主桌面会话。"
                          + "BetterGI 将短暂断开当前用户的活动后台会话，并重启 UU 串流进程，"
                          + "当前 UU 远程画面可能中断数秒。不会修改系统安全策略，修复失败时仍会继续启动桌面分身。";
            var notification = RaiseState(UuRemoteRepairState.Repairing, warning);
            await notification.PresentationCompleted.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var result = await RepairCoreAsync(snapshot, assessment, cancellationToken).ConfigureAwait(false);
            RaiseState(result.IsSuccessful ? UuRemoteRepairState.Succeeded : UuRemoteRepairState.Failed,
                result.IsSuccessful ? "UU 远程主会话冲突已修复，正在继续启动桌面分身。"
                    : result.FailureReason ?? "UU 远程主会话修复失败。");
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RaiseState(UuRemoteRepairState.Idle, string.Empty);
            throw;
        }
        catch (Exception exception)
        {
            return ReportFailure([], $"UU 远程主会话修复失败：{exception.GetBaseException().Message}", exception);
        }
        finally
        {
            _isRepairing = false;
            _repairGate.Release();
        }
    }

    /// <summary>登录后短期观察；启动前没有冲突时允许首次延迟修复，已有尝试时仅通知。</summary>
    internal Task<UuRemoteSessionRepairResult> CheckAfterStartAsync(CancellationToken cancellationToken)
        => Task.Run(async () =>
        {
            try
            {
                // 使用单调时钟，避免系统时间校准让轮询无限延长或提前结束。
                var startedAt = _timeProvider.GetTimestamp();
                while (IsEnabled && _timeProvider.GetElapsedTime(startedAt) < StartupMonitorDuration)
                {
                    await Task.Delay(PollInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
                    if (!IsEnabled)
                    {
                        break;
                    }

                    var snapshot = await InspectAsync(cancellationToken).ConfigureAwait(false);
                    if (!UuRemoteSessionEvaluator.Evaluate(snapshot).HasConflict)
                    {
                        continue;
                    }

                    return await TryRepairBeforeStartAsync(cancellationToken).ConfigureAwait(false);
                }

                return UuRemoteSessionRepairResult.NotNeeded();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "桌面分身启动后复查 UU 远程会话失败");
                return UuRemoteSessionRepairResult.NotNeeded();
            }
        }, cancellationToken);

    /// <summary>每次破坏性操作前重新检查权限、控制台及其他用户，并等待 UU 返回控制台。</summary>
    private async Task<UuRemoteSessionRepairResult> RepairCoreAsync(
        UuRemoteSessionSnapshot originalSnapshot,
        UuRemoteSessionAssessment originalAssessment,
        CancellationToken cancellationToken)
    {
        var originalSessions = originalAssessment.GameViewerSessions;
        var disconnectedUserSessions = false;
        try
        {
            // 即使第一次快照没有待断开的会话，也必须复核其他用户和控制台状态。
            var snapshot = await InspectAsync(cancellationToken).ConfigureAwait(false);
            var assessment = UuRemoteSessionEvaluator.Evaluate(snapshot);
            var reason = GetSafetyBlockingReason(snapshot, assessment, originalSnapshot.ConsoleSessionId);
            if (reason is not null)
            {
                return Fail(reason);
            }

            if (!assessment.HasConflict && assessment.HasUuStreamingProcess)
            {
                return UuRemoteSessionRepairResult.Successful(originalSessions,
                    snapshot.GameViewerServers.Select(x => x.SessionId).Distinct().ToArray());
            }

            var sessionsToDisconnect = assessment.SessionsToDisconnect;
            foreach (var sessionId in sessionsToDisconnect)
            {
                snapshot = await InspectAsync(cancellationToken).ConfigureAwait(false);
                assessment = UuRemoteSessionEvaluator.Evaluate(snapshot);
                reason = GetSafetyBlockingReason(snapshot, assessment, originalSnapshot.ConsoleSessionId);
                if (reason is not null)
                {
                    return Fail(reason);
                }

                var session = snapshot.Sessions.FirstOrDefault(x => x.SessionId == sessionId);
                if (session is null || !session.IsActive)
                {
                    continue;
                }

                if (session.IsConsole || session.SessionId == snapshot.ConsoleSessionId
                    || !UuRemoteSessionEvaluator.IsCurrentUser(snapshot, session))
                {
                    return Fail($"Windows 会话 {sessionId} 的类型或所有者已变化，已取消本次修复。");
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!_platform.DisconnectSession(sessionId, out var errorCode))
                {
                    return Fail($"无法断开当前用户的后台 Windows 会话 {sessionId}。Win32 错误：{errorCode}。");
                }

                disconnectedUserSessions = true;
                _logger.LogInformation("已请求断开当前用户的后台 Windows 会话 {SessionId}。", sessionId);
            }

            if (disconnectedUserSessions)
            {
                await Task.Delay(SessionDisconnectSettleDelay, _timeProvider, cancellationToken).ConfigureAwait(false);
            }

            // WTSDisconnectSession 是异步请求。确认所有后台会话已断开，再重启 UU 进程。
            var disconnectStartedAt = _timeProvider.GetTimestamp();
            while (true)
            {
                snapshot = await InspectAsync(cancellationToken).ConfigureAwait(false);
                assessment = UuRemoteSessionEvaluator.Evaluate(snapshot);
                reason = GetSafetyBlockingReason(snapshot, assessment, originalSnapshot.ConsoleSessionId);
                if (reason is not null)
                {
                    return Fail(reason);
                }

                if (assessment.SessionsToDisconnect.Count == 0)
                {
                    break;
                }

                if (_timeProvider.GetElapsedTime(disconnectStartedAt) >= SessionDisconnectTimeout)
                {
                    return Fail("当前用户的后台 Windows 会话尚未完成断开，已跳过 UU 串流进程重启。");
                }

                await Task.Delay(PollInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await _platform.StopStreamingProcessesAsync(originalSnapshot.ConsoleSessionId!.Value,
                ValidateBeforeStoppingAsync, cancellationToken).ConfigureAwait(false);

            var restartStartedAt = _timeProvider.GetTimestamp();
            IReadOnlyList<int> lastSessions = [];
            while (_timeProvider.GetElapsedTime(restartStartedAt) < StreamingProcessRestartTimeout)
            {
                await Task.Delay(PollInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
                snapshot = await InspectAsync(cancellationToken).ConfigureAwait(false);
                assessment = UuRemoteSessionEvaluator.Evaluate(snapshot);
                reason = GetSafetyBlockingReason(snapshot, assessment, originalSnapshot.ConsoleSessionId);
                if (reason is not null)
                {
                    return Fail(reason);
                }

                lastSessions = snapshot.GameViewerServers.Select(x => x.SessionId).Distinct().OrderBy(x => x).ToArray();
                if (lastSessions.Count > 0 && lastSessions.All(x => x == originalSnapshot.ConsoleSessionId))
                {
                    _logger.LogInformation("UU 远程主会话修复成功，GameViewerServer 已回到会话 {SessionId}。",
                        snapshot.ConsoleSessionId);
                    return UuRemoteSessionRepairResult.Successful(originalSessions, lastSessions)
                        with { DisconnectedUserSessions = disconnectedUserSessions };
                }
            }

            return Fail($"UU 串流进程未能在 {StreamingProcessRestartTimeout.TotalSeconds:0} 秒内回到主桌面会话。"
                        + (lastSessions.Count == 0 ? "当前未检测到 GameViewerServer 进程。"
                            : $"当前会话：{string.Join(", ", lastSessions)}。"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "UU 远程主会话修复失败");
            return Fail(exception.GetBaseException().Message);
        }

        // 部分断开后发生错误时也要让调用方恢复 RDP，不能只在成功时设置该标志。
        UuRemoteSessionRepairResult Fail(string reason)
        {
            _logger.LogWarning("UU 远程主会话修复已停止：{Reason}。", reason);
            return UuRemoteSessionRepairResult.Failed(originalSessions, reason)
                with { DisconnectedUserSessions = disconnectedUserSessions };
        }

        // 停止多个串流进程时逐个复核，防止等待旧进程退出期间用户或会话状态发生变化。
        async Task ValidateBeforeStoppingAsync(CancellationToken token)
        {
            var latestSnapshot = await InspectAsync(token).ConfigureAwait(false);
            var latestAssessment = UuRemoteSessionEvaluator.Evaluate(latestSnapshot);
            var reason = GetSafetyBlockingReason(latestSnapshot, latestAssessment,
                originalSnapshot.ConsoleSessionId);
            if (reason is not null)
            {
                throw new InvalidOperationException(reason);
            }

            if (latestAssessment.SessionsToDisconnect.Count > 0)
            {
                throw new InvalidOperationException("修复期间出现新的活动后台 Windows 会话，已停止 UU 串流进程重启。");
            }
        }
    }

    /// <summary>复核操作资格、用户身份、控制台切换及开关状态。</summary>
    private string? GetSafetyBlockingReason(
        UuRemoteSessionSnapshot snapshot,
        UuRemoteSessionAssessment assessment,
        uint? expectedConsoleSessionId)
    {
        if (!IsEnabled)
        {
            return "自动修复已关闭，本次 UU 会话修复已停止。";
        }

        if (!_platform.IsRoot)
        {
            return "只有 BetterGI 根实例可以修复 UU 远程主会话。";
        }

        if (!_platform.IsElevated)
        {
            return "BetterGI 需要以管理员权限运行才能修复 UU 远程主会话。";
        }

        if (snapshot.ConsoleSessionId is null || snapshot.ConsoleSessionId != expectedConsoleSessionId
            || snapshot.CurrentSessionId != snapshot.ConsoleSessionId)
        {
            return "主桌面会话无法确定或已发生变化，BetterGI 必须位于主桌面才能修复 UU 会话。";
        }

        return assessment.BlockingReason;
    }

    /// <summary>统一报告尚未进行会话操作时的检查失败。</summary>
    private UuRemoteSessionRepairResult ReportFailure(
        IReadOnlyList<int> originalSessions, string reason, Exception? exception = null)
    {
        _logger.LogWarning(exception, "跳过 UU 远程主会话修复：{Reason}。", reason);
        RaiseState(UuRemoteRepairState.Failed, reason);
        return UuRemoteSessionRepairResult.Failed(originalSessions, reason);
    }

    /// <summary>格式化会话诊断，不记录用户密码或令牌。</summary>
    private static string FormatSessions(IReadOnlyList<WindowsSessionInfo> sessions)
        => string.Join("; ", sessions.Select(x => $"{x.SessionId}:{x.State}:{x.UserName}"));

    /// <summary>同步发出事件，并允许 UI 为修复前风险提示提供异步呈现确认。</summary>
    private UuRemoteRepairStateChangedEventArgs RaiseState(UuRemoteRepairState state, string message)
    {
        var args = new UuRemoteRepairStateChangedEventArgs(state, message);
        RepairStateChanged?.Invoke(this, args);
        return args;
    }
}
