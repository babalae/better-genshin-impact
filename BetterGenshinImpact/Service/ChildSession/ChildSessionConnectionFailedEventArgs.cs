using System;

namespace BetterGenshinImpact.Service.ChildSession;

public sealed class ChildSessionConnectionFailedEventArgs(
    string message,
    int errorCode,
    int? extendedErrorCode = null) : EventArgs
{
    public string Message { get; } = message;

    public int ErrorCode { get; } = errorCode;

    public int? ExtendedErrorCode { get; } = extendedErrorCode;

    /// <summary>
    /// 这次失败出在凭据上（密码被拒、账户被锁/禁用/过期，或旧密码清不掉），
    /// 不是网络或协议问题。
    ///
    /// 由上报方判定：登录事件和断开事件的错误码取值范围重叠
    /// （比如 0 在两边含义完全不同），只能在知道来源的地方判断，事后无法反推。
    /// </summary>
    public bool IsLogonFailure { get; init; }
}
