using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Infrastructure.NetworkRecovery;

public enum LoginRecoveryState
{
    Idle,
    Detecting,
    ConfirmingNetworkError,
    ReturningToMainUi,
    Relogging,
    Succeeded,
    Failed,
    Cancelled
}

public enum LoginScreenState
{
    Unknown,
    MainUi,
    NetworkError,
    LoginRequired
}

public readonly record struct LoginRecoveryResult(
    bool Succeeded,
    LoginRecoveryState State,
    string Message);

public interface ILoginAdapter
{
    string Name { get; }
    bool CanHandle();
    Task<LoginScreenState> DetectAsync(CancellationToken cancellationToken);
    Task<bool> ConfirmNetworkErrorAsync(CancellationToken cancellationToken);
    Task<bool> ReturnToMainUiAsync(CancellationToken cancellationToken);
    Task<bool> ReloginAsync(CancellationToken cancellationToken);
}

public interface ILoginRecoveryStateMachine
{
    LoginRecoveryState State { get; }
    Task<LoginRecoveryResult> RecoverAsync(CancellationToken cancellationToken);
}
