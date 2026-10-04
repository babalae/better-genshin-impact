using System;

namespace BetterGenshinImpact.Service.Worker;

/// <summary>
/// Worker 的授权策略。纯函数，便于单独验证；平台层由管道 ACL 与客户端 SID 模拟共同保证。
/// </summary>
internal static class WorkerAuthorization
{
    /// <summary>
    /// 只有 Worker 自身用户与显式配置的 Controller 用户可以通过。
    /// <paramref name="clientSid"/> 必须来自命名管道内核信息，不能使用客户端上报值。
    /// </summary>
    public static bool IsControllerAllowed(
        string workerSid,
        string? controllerSid,
        string clientSid)
    {
        if (string.IsNullOrWhiteSpace(clientSid))
        {
            return false;
        }

        if (string.Equals(clientSid, workerSid, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(controllerSid)
               && string.Equals(clientSid, controllerSid, StringComparison.OrdinalIgnoreCase);
    }
}
