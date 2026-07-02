namespace BetterGenshinImpact.Core.CloudGame;

/// <summary>
/// 云原神页面输入链路的健康状态。
/// 只有注入脚本、webpack ClientCore、RTC DataChannel 和游戏数据通道全部就绪时，
/// <see cref="IsReady"/> 才会返回 <see langword="true"/>。
/// </summary>
public sealed class CloudGameHealth
{
    /// <summary>
    /// 页面是否同时存在附件脚本和 BetterGI 稳定包装层。
    /// </summary>
    public bool Installed { get; set; }

    /// <summary>
    /// 当前附件脚本报告的版本号。
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// RTC DataChannel 的 readyState。
    /// </summary>
    public string? RtcDataChannelState { get; set; }

    /// <summary>
    /// 云游戏数据通道是否已经开始传输游戏数据。
    /// </summary>
    public bool? GameDataStarted { get; set; }

    /// <summary>
    /// ClientCore 状态机当前状态，主要用于诊断页面连接阶段。
    /// </summary>
    public string? StateMachine { get; set; }

    /// <summary>
    /// 健康检查失败时由页面返回的错误信息。
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// 获取当前页面是否已经具备执行云输入命令的条件。
    /// </summary>
    public bool IsReady => Installed
                           && Error == null
                           && RtcDataChannelState == "open"
                           && GameDataStarted == true;
}
