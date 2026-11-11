using System;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Service.Instance;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 提供当前进程的实例键。Pulonia 服务通过它取得“本实例”，不直接读取静态的实例上下文，测试可替换为固定值。
/// </summary>
public interface IPuloniaInstanceKeyProvider
{
    /// <summary>当前进程的实例键：<c>primary</c>、<c>child_session</c> 或 <c>webview:&lt;小写实例名&gt;</c>。</summary>
    string CurrentInstanceKey { get; }
}

/// <summary>
/// 固定实例键的提供者：生产环境由实例上下文映射得到，测试直接传入实例键。
/// </summary>
public sealed class PuloniaInstanceKeyProvider : IPuloniaInstanceKeyProvider
{
    /// <inheritdoc />
    public string CurrentInstanceKey { get; }

    /// <summary>
    /// 使用已知实例键建立提供者。
    /// </summary>
    /// <param name="instanceKey">合法的实例键</param>
    public PuloniaInstanceKeyProvider(string instanceKey)
    {
        PuloniaInstanceKey.Validate(instanceKey);
        CurrentInstanceKey = instanceKey;
    }

    /// <summary>
    /// 由实例上下文建立提供者。
    /// </summary>
    /// <param name="context">当前进程的实例上下文</param>
    /// <returns>实例键提供者</returns>
    public static PuloniaInstanceKeyProvider FromContext(InstanceContext context)
        => new(GetInstanceKey(context.InstanceType, context.InstanceName));

    /// <summary>
    /// 把实例类型与实例名映射到实例键：主实例 <c>primary</c>，桌面分身 <c>child_session</c>，云原神 <c>webview:&lt;小写实例名&gt;</c>。
    /// </summary>
    /// <param name="instanceType">实例类型</param>
    /// <param name="instanceName">云原神实例名；其他实例为空</param>
    /// <returns>实例键</returns>
    public static string GetInstanceKey(BetterGiInstanceType instanceType, string? instanceName) => instanceType switch
    {
        BetterGiInstanceType.Primary => PuloniaInstanceKey.Primary,
        BetterGiInstanceType.ChildSession => PuloniaInstanceKey.ChildSession,
        BetterGiInstanceType.WebView => PuloniaInstanceKey.ForWebView(instanceName ?? string.Empty),
        _ => throw new ArgumentOutOfRangeException(nameof(instanceType), instanceType, "未知的实例类型。")
    };
}
