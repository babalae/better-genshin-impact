using System;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 账号准备或核验失败：没有在错误账号上执行业务，整次运行必须进入待处理，不能作为普通节点失败继续或重试。
/// 主实例本地执行与子实例远程执行共用；子实例把它回报为 AccountFailed。
/// </summary>
/// <param name="message">面向用户的失败原因。</param>
/// <param name="inner">原始异常。</param>
internal sealed class PuloniaGenshinAccountException(string message, Exception inner) : Exception(message, inner);
