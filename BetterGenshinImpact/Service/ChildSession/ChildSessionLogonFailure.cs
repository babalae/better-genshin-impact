namespace BetterGenshinImpact.Service.ChildSession;

/// <summary>
/// 判断一个 RDP 错误码是否表示「凭据被拒」。
///
/// 这类失败重试没有意义：每次重试都拿同一个密码再撞一次，只会把账户往锁定策略上推
/// （常见锁定阈值就 3～5 次），所以调用方要据此跳过重试，直接交给用户改密码。
///
/// 两套码值必须分开判断，不能混用——同一个小整数在两条上报路径上含义完全不同：
/// <c>OnLogonError</c> 的 0 是「密码错误」，<c>OnDisconnected</c> 的 0 是「无信息」。
/// </summary>
internal static class ChildSessionLogonFailure
{
    /// <summary>
    /// <c>IMsTscAxEvents::OnLogonError</c> 的 <c>lError</c> 是否表示凭据被拒。
    ///
    /// 只认「密码错误」和「密码必须修改」两种；<c>2</c>（LOGON_FAILED_OTHER）是
    /// 「其它登录或登录后错误」，并不说明密码不对，仍应允许有限次重试。
    /// </summary>
    internal static bool IsRejectedLogonCode(int errorCode)
    {
        return errorCode switch
        {
            0 => true, // LOGON_FAILED_BAD_PASSWORD：凭据无效
            1 => true, // LOGON_FAILED_UPDATE_PASSWORD：密码过期，必须改密
            _ => IsAccountLevelFailure(errorCode)
        };
    }

    /// <summary>
    /// <c>IMsTscAxEvents::OnDisconnected</c> 的 <c>discReason</c>（或扩展原因）
    /// 是否表示凭据被拒。
    ///
    /// 主机在 NLA 阶段直接拒绝时不一定先发 <c>OnLogonError</c>，断开原因才是唯一线索。
    /// 这里不认 <c>SSL_ERR_NO_AUTHENTICATING_AUTHORITY</c>（6151）：那是联系不上验证方
    /// （域不可达、信任关系失效），属于可能自行恢复的临时故障，值得重试。
    /// </summary>
    internal static bool IsRejectedDisconnectCode(int reason)
    {
        return reason switch
        {
            2055 => true, // SSL_ERR_LOGON_FAILURE
            2567 => true, // SSL_ERR_NO_SUCH_USER
            2823 => true, // SSL_ERR_ACCOUNT_DISABLED
            3079 => true, // SSL_ERR_ACCOUNT_RESTRICTION
            3335 => true, // SSL_ERR_ACCOUNT_LOCKED_OUT
            3591 => true, // SSL_ERR_ACCOUNT_EXPIRED
            3847 => true, // SSL_ERR_PASSWORD_EXPIRED
            4615 => true, // SSL_ERR_PASSWORD_MUST_CHANGE
            5895 => true, // SSL_ERR_POLICY_NTLM_ONLY
            8455 => true, // SSL_ERR_FRESH_CRED_REQUIRED_BY_SERVER：服务器不接受保存的凭据
            _ => IsAccountLevelFailure(reason)
        };
    }

    /// <summary>
    /// 两条路径共用的 NTSTATUS 码。扩展断开原因也可能直接带这些值。
    /// </summary>
    private static bool IsAccountLevelFailure(int code)
    {
        return code is unchecked((int)0xC000006A) // STATUS_WRONG_PASSWORD
            or unchecked((int)0xC000006D)         // STATUS_LOGON_FAILURE
            or unchecked((int)0xC000006E)         // STATUS_ACCOUNT_RESTRICTION
            or unchecked((int)0xC0000072)         // STATUS_ACCOUNT_DISABLED
            or unchecked((int)0xC0000224)         // STATUS_PASSWORD_MUST_CHANGE
            or unchecked((int)0xC0000234);        // STATUS_ACCOUNT_LOCKED_OUT
    }
}
