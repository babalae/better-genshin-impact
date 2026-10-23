using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Service.Instance;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 新一条龙“运行实例”下拉中的一项
/// </summary>
/// <param name="display">显示名</param>
/// <param name="target">执行目标；主实例为 null</param>
public sealed class PuloniaOneDragonTargetOption(string display, PuloniaTaskExecutionTarget? target)
{
    /// <summary>
    /// 显示名
    /// </summary>
    public string Display { get; } = display;

    /// <summary>
    /// 执行目标；主实例本地执行时为 null
    /// </summary>
    public PuloniaTaskExecutionTarget? Target { get; } = target;

    /// <summary>
    /// 执行通道标识，刷新列表时据此保留选择
    /// </summary>
    public string LaneKey => PuloniaTaskExecutionTarget.GetLaneKey(Target);

    /// <summary>
    /// 主实例本地执行
    /// </summary>
    public static PuloniaOneDragonTargetOption Primary { get; } = new("主实例", null);

    /// <summary>
    /// 由在线子实例生成选项；只有桌面分身和云原神可以执行计划
    /// </summary>
    /// <param name="endpoint">在线子实例</param>
    /// <returns>选项；不支持的实例类型返回 null</returns>
    public static PuloniaOneDragonTargetOption? FromEndpoint(InstanceEndpoint endpoint)
    {
        PuloniaTaskExecutionTarget? target = endpoint.InstanceType switch
        {
            BetterGiInstanceType.ChildSession => new PuloniaTaskExecutionTarget
            {
                Kind = PuloniaTaskExecutionTargetKind.ChildSession,
                WindowsSessionId = endpoint.WindowsSessionId,
                DisplayName = $"桌面分身（会话 {endpoint.WindowsSessionId}）"
            },
            BetterGiInstanceType.WebView when !string.IsNullOrWhiteSpace(endpoint.InstanceName) => new PuloniaTaskExecutionTarget
            {
                Kind = PuloniaTaskExecutionTargetKind.WebView,
                InstanceName = endpoint.InstanceName,
                DisplayName = $"云原神（{endpoint.InstanceName}）"
            },
            _ => null
        };
        return target is null ? null : new PuloniaOneDragonTargetOption(target.DisplayName, target);
    }
}
