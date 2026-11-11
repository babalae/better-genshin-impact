using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel;

/// <summary>
/// 主窗口标题栏中一个在线子实例的展示项。实例身份只读；Pulonia 当前账户由主窗口按实例键读取共享设置后填入。
/// </summary>
public sealed partial class ConnectedInstanceItem : ObservableObject
{
    /// <summary>
    /// 标题栏直接展示的最大实例图标数量。
    /// </summary>
    internal const int MaximumVisibleItems = 3;

    /// <summary>
    /// 不含账户的悬浮提示，账户变化时在其后追加当前账户。
    /// </summary>
    private readonly string _baseToolTip;

    /// <summary>
    /// 子实例的当前账户名称；账户目录只有一个账户、实例键无法确定或尚未读取时为 null，不显示账户部分。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccount))]
    [NotifyPropertyChangedFor(nameof(AccountMenuHeader))]
    [NotifyPropertyChangedFor(nameof(MenuText))]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    private string? _accountName;

    /// <summary>
    /// 子实例正在运行的 Pulonia 计划名（取自它 state.json 的活动运行，或主实例下发跟踪器）；没有活动运行或读不出时为 null。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(MenuText))]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    private string? _runningPlanName;

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
        _baseToolTip = $"Session {endpoint.WindowsSessionId} · PID {endpoint.ProcessId}";
        InstanceKey = TryGetInstanceKey(endpoint);
    }

    /// <summary>
    /// 子实例的 IPC 端点，用于点击时重新校验并激活当前连接。
    /// </summary>
    public InstanceEndpoint Endpoint { get; }

    /// <summary>
    /// 子实例的 Pulonia 实例键，用于读写它的当前账户；无法确定（例如云原神实例缺少实例名）时为 null。
    /// </summary>
    public string? InstanceKey { get; }

    /// <summary>
    /// 子实例类型对应的 WPF-UI 图标。
    /// </summary>
    public SymbolRegular Icon { get; }

    /// <summary>
    /// 标题栏菜单中显示的实例名称。
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// 实例图标的完整悬浮提示；有账户部分时包含当前账户，正在运行计划时包含计划名。
    /// </summary>
    public string ToolTip
    {
        get
        {
            var lines = new List<string> { DisplayName };
            if (AccountName is { } account)
                lines.Add("当前账户：" + account);
            if (RunningPlanName is { } plan)
                lines.Add("正在运行：" + plan);
            lines.Add(_baseToolTip);
            return string.Join("\n", lines);
        }
    }

    /// <summary>是否显示账户部分。</summary>
    public bool HasAccount => AccountName is not null;

    /// <summary>是否正在运行 Pulonia 计划。</summary>
    public bool IsRunning => RunningPlanName is not null;

    /// <summary>右键菜单中修改当前账户的子菜单标题。</summary>
    public string AccountMenuHeader => "当前账户：" + AccountName;

    /// <summary>“+N”列表中显示的文字：实例名，有账户部分时加“ — 账户名”，正在运行计划时加“ · 运行中：计划名”。</summary>
    public string MenuText
    {
        get
        {
            var text = AccountName is { } account ? $"{DisplayName} — {account}" : DisplayName;
            return RunningPlanName is { } plan ? $"{text} · 运行中：{plan}" : text;
        }
    }

    /// <summary>可改为的账户，选择即直接写入该子实例的共享设置文件。</summary>
    public ObservableCollection<PuloniaAccountChoiceViewModel> AccountOptions { get; } = [];

    /// <summary>
    /// 填入子实例的当前账户与可选账户。
    /// </summary>
    /// <param name="accountName">当前账户名称；“未确定”时传入“未确定”</param>
    /// <param name="options">可改为的账户</param>
    internal void SetAccount(string accountName, IEnumerable<PuloniaAccountChoiceViewModel> options)
    {
        AccountOptions.Clear();
        foreach (var option in options)
            AccountOptions.Add(option);
        AccountName = accountName;
    }

    /// <summary>
    /// 不显示账户部分（账户目录只有一个账户或读取失败）。
    /// </summary>
    internal void ClearAccount()
    {
        AccountOptions.Clear();
        AccountName = null;
    }

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

    /// <summary>
    /// 由端点得到 Pulonia 实例键；类型未知或云原神实例名不合法时返回 null。
    /// </summary>
    /// <param name="endpoint">在线子实例端点。</param>
    private static string? TryGetInstanceKey(InstanceEndpoint endpoint)
    {
        if (endpoint.InstanceType is not (BetterGiInstanceType.ChildSession or BetterGiInstanceType.WebView))
        {
            return null;
        }

        try
        {
            return PuloniaInstanceKeyProvider.GetInstanceKey(endpoint.InstanceType, endpoint.InstanceName);
        }
        catch (PuloniaTaskValidationException)
        {
            return null;
        }
    }
}
