using System;

namespace BetterGenshinImpact.Service.HoYoLab;

/// <summary>
/// HoYoLAB 登录已失效（Cookie 过期或被注销），需要重新登录
/// </summary>
public sealed class HoYoLabSessionExpiredException(string message) : Exception(message);

/// <summary>
/// HoYoLAB 接口返回非 0 返回码
/// </summary>
/// <param name="retcode">接口返回码</param>
/// <param name="message">接口返回的消息</param>
public sealed class HoYoLabApiException(int retcode, string message) : Exception($"retcode={retcode}: {message}")
{
    /// <summary>
    /// 接口返回码
    /// </summary>
    public int Retcode { get; } = retcode;
}
