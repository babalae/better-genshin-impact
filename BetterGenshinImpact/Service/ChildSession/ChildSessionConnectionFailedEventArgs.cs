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

    /// <summary>
    /// 这次失败可以通过重建 RDP 控件恢复。
    ///
    /// 典型是「控件里残留了清不掉的密码」：同一个控件重试多少次都一样，但换一个
    /// 全新的控件就能干净地进入手动输入流程。服务侧收到后不报失败，直接重建重连。
    /// </summary>
    public bool RecoverableByRecreatingHost { get; init; }

    /// <summary>
    /// ActiveX 显示了一个交互式登录界面在等用户重新输入（LOGON_FAILED_OTHER = 2）。
    ///
    /// 这不是连接失败：连接是通的，只是凭据没通过、控件弹了输入界面。服务侧据此
    /// 豁免连接超时，和「改密界面」一样保持连接等用户操作。
    /// </summary>
    public bool IsInteractiveLogon { get; init; }
}
