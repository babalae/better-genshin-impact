using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Infrastructure.NetworkRecovery;

public sealed class LoginRecoveryStateMachine : ILoginRecoveryStateMachine
{
    private readonly IReadOnlyList<ILoginAdapter> _adapters;
    private readonly IRecoverySession _recoverySession;
    private readonly INetworkPauseGate _networkPauseGate;
    private readonly ILogger<LoginRecoveryStateMachine> _logger;
    private LoginRecoveryState _state = LoginRecoveryState.Idle;

    public LoginRecoveryStateMachine(
        IEnumerable<ILoginAdapter> adapters,
        IRecoverySession recoverySession,
        INetworkPauseGate networkPauseGate,
        ILogger<LoginRecoveryStateMachine> logger)
    {
        _adapters = adapters.ToList();
        _recoverySession = recoverySession;
        _networkPauseGate = networkPauseGate;
        _logger = logger;
    }

    public LoginRecoveryState State => _state;

    public async Task<LoginRecoveryResult> RecoverAsync(CancellationToken cancellationToken)
    {
        using var lease = _recoverySession.BeginRecovery();
        if (lease is null)
        {
            return new LoginRecoveryResult(false, LoginRecoveryState.Idle, "恢复流程已在运行");
        }

        try
        {
            var adapter = _adapters.FirstOrDefault(item => item.CanHandle());
            if (adapter is null)
            {
                return Fail("没有可用的登录适配器");
            }

            SetState(LoginRecoveryState.Detecting);
            var screen = await adapter.DetectAsync(cancellationToken);
            if (screen == LoginScreenState.NetworkError)
            {
                SetState(LoginRecoveryState.ConfirmingNetworkError);
                var confirmed = await adapter.ConfirmNetworkErrorAsync(cancellationToken);
                if (!confirmed)
                {
                    return Fail("未能确认网络错误提示");
                }

                screen = await adapter.DetectAsync(cancellationToken);
            }

            SetState(LoginRecoveryState.ReturningToMainUi);
            var atMainUi = screen == LoginScreenState.MainUi ||
                           await adapter.ReturnToMainUiAsync(cancellationToken);
            if (!atMainUi || screen == LoginScreenState.LoginRequired)
            {
                SetState(LoginRecoveryState.Relogging);
                atMainUi = await adapter.ReloginAsync(cancellationToken);
            }

            if (!atMainUi)
            {
                return Fail("恢复后未检测到游戏主界面");
            }

            _networkPauseGate.ClearNetworkPause();
            SetState(LoginRecoveryState.Succeeded);
            _logger.LogInformation("网络恢复完成，当前任务上下文将继续执行");
            return new LoginRecoveryResult(true, State, "网络恢复成功");
        }
        catch (OperationCanceledException)
        {
            SetState(LoginRecoveryState.Cancelled);
            return new LoginRecoveryResult(false, State, "网络恢复已取消");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "登录恢复状态机执行失败");
            return Fail(e.Message);
        }
        finally
        {
            if (State is not LoginRecoveryState.Succeeded)
            {
                _logger.LogWarning("网络恢复未完成，保留暂停门控等待下一次探测");
            }
        }
    }

    private LoginRecoveryResult Fail(string message)
    {
        SetState(LoginRecoveryState.Failed);
        return new LoginRecoveryResult(false, State, message);
    }

    private void SetState(LoginRecoveryState state) => _state = state;
}
