using BetterGenshinImpact.Service.Instance;
using System.Collections.Generic;
using System.Linq;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel;

/// <summary>
/// 主窗口标题栏中一个在线子实例的只读展示项。
/// </summary>
public sealed class ConnectedInstanceItem
{
    /// <summary>
    /// 标题栏直接展示的最大实例图标数量。
    /// </summary>
    internal const int MaximumVisibleItems = 3;

    /// <summary>
    /// 创建一个在线子实例展示项。
    /// </summary>
    /// <param name="endpoint">已完成 IPC 登记的子实例端点。</param>
    public ConnectedInstanceItem(InstanceEndpoint endpoint)
    {
        Endpoint = endpoint;
        Icon = endpoint.InstanceType == BetterGiInstanceType.ChildSession
            ? SymbolRegular.DesktopCursor24
            : SymbolRegular.Globe24;
        DisplayName = CreateDisplayName(endpoint);
        ToolTip = $"{DisplayName}\nSession {endpoint.WindowsSessionId} · PID {endpoint.ProcessId}";
    }

    /// <summary>
    /// 子实例的 IPC 端点，用于点击时重新校验并激活当前连接。
    /// </summary>
    public InstanceEndpoint Endpoint { get; }

    /// <summary>
    /// 子实例类型对应的 WPF-UI 图标。
    /// </summary>
    public SymbolRegular Icon { get; }

    /// <summary>
    /// 标题栏菜单中显示的实例名称。
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// 实例图标的完整悬浮提示。
    /// </summary>
    public string ToolTip { get; }

    /// <summary>
    /// 将已排序的端点快照转换成标题栏展示项。
    /// </summary>
    /// <param name="endpoints">在线子实例端点集合。</param>
    internal static IReadOnlyList<ConnectedInstanceItem> CreateItems(
        IEnumerable<InstanceEndpoint> endpoints)
    {
        return endpoints.Select(endpoint => new ConnectedInstanceItem(endpoint)).ToArray();
    }

    /// <summary>
    /// 取得标题栏中直接显示的前三个实例。
    /// </summary>
    /// <param name="items">全部在线子实例展示项。</param>
    internal static IReadOnlyList<ConnectedInstanceItem> GetVisibleItems(
        IReadOnlyList<ConnectedInstanceItem> items)
    {
        return items.Take(MaximumVisibleItems).ToArray();
    }

    /// <summary>
    /// 计算需要收进“+N”菜单的实例数量。
    /// </summary>
    /// <param name="items">全部在线子实例展示项。</param>
    internal static int GetOverflowCount(IReadOnlyCollection<ConnectedInstanceItem> items)
    {
        return int.Max(0, items.Count - MaximumVisibleItems);
    }

    /// <summary>
    /// 根据实例类型生成适合标题栏展示的名称。
    /// </summary>
    /// <param name="endpoint">在线子实例端点。</param>
    private static string CreateDisplayName(InstanceEndpoint endpoint)
    {
        return endpoint.InstanceType switch
        {
            BetterGiInstanceType.ChildSession => $"桌面分身（Session {endpoint.WindowsSessionId}）",
            BetterGiInstanceType.WebView when !string.IsNullOrWhiteSpace(endpoint.InstanceName) =>
                $"云原神 · {endpoint.InstanceName}",
            BetterGiInstanceType.WebView => $"云原神（进程 {endpoint.ProcessId}）",
            _ => $"子实例（进程 {endpoint.ProcessId}）"
        };
    }
}
