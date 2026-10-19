using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.ViewModel;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.UnitTest.ServiceTests.Instance;

/// <summary>
/// 验证主窗口标题栏在线子实例的排序与展示投影。
/// </summary>
public class ConnectedInstanceItemTests
{
    /// <summary>
    /// 桌面分身应排在云原神之前，同类实例保持稳定顺序。
    /// </summary>
    [Fact]
    public void OrderConnectedChildInstances_ShouldPlaceChildSessionsBeforeNamedWebViews()
    {
        var endpoints = new[]
        {
            CreateEndpoint(BetterGiInstanceType.WebView, 40, 1, "账号B"),
            CreateEndpoint(BetterGiInstanceType.ChildSession, 30, 3),
            CreateEndpoint(BetterGiInstanceType.WebView, 20, 1, "账号A"),
            CreateEndpoint(BetterGiInstanceType.ChildSession, 10, 2)
        };

        var result = InstanceService.OrderConnectedChildInstances(endpoints);

        Assert.Collection(
            result,
            endpoint => Assert.Equal(2, endpoint.WindowsSessionId),
            endpoint => Assert.Equal(3, endpoint.WindowsSessionId),
            endpoint => Assert.Equal("账号A", endpoint.InstanceName),
            endpoint => Assert.Equal("账号B", endpoint.InstanceName));
    }

    /// <summary>
    /// 不同实例类型应生成对应图标、名称和完整进程提示。
    /// </summary>
    [Fact]
    public void Constructor_ShouldMapInstanceTypeToDisplayProperties()
    {
        var child = new ConnectedInstanceItem(
            CreateEndpoint(BetterGiInstanceType.ChildSession, 101, 8));
        var webView = new ConnectedInstanceItem(
            CreateEndpoint(BetterGiInstanceType.WebView, 202, 1, "小号"));

        Assert.Equal(SymbolRegular.DesktopCursor24, child.Icon);
        Assert.Equal("桌面分身（Session 8）", child.DisplayName);
        Assert.Contains("PID 101", child.ToolTip);
        Assert.Equal(SymbolRegular.Globe24, webView.Icon);
        Assert.Equal("云原神 · 小号", webView.DisplayName);
        Assert.Contains("Session 1", webView.ToolTip);
    }

    /// <summary>
    /// 超过三个实例时只直接展示前三个，并正确计算“+N”数量。
    /// </summary>
    [Fact]
    public void VisibleItems_ShouldLimitToThreeAndCalculateOverflow()
    {
        var items = Enumerable.Range(1, 5)
            .Select(index => new ConnectedInstanceItem(
                CreateEndpoint(BetterGiInstanceType.WebView, index, 1, $"账号{index}")))
            .ToArray();

        var visibleItems = ConnectedInstanceItem.GetVisibleItems(items);
        var overflowCount = ConnectedInstanceItem.GetOverflowCount(items);

        Assert.Equal(3, visibleItems.Count);
        Assert.Equal(2, overflowCount);
        Assert.Equal("云原神 · 账号1", visibleItems[0].DisplayName);
    }

    /// <summary>
    /// 创建用于展示测试的实例端点。
    /// </summary>
    /// <param name="instanceType">实例类型。</param>
    /// <param name="processId">进程标识。</param>
    /// <param name="sessionId">Windows Session 标识。</param>
    /// <param name="instanceName">可选的云原神实例名。</param>
    private static InstanceEndpoint CreateEndpoint(
        BetterGiInstanceType instanceType,
        int processId,
        int sessionId,
        string? instanceName = null)
    {
        return new InstanceEndpoint
        {
            InstanceType = instanceType,
            ProcessId = processId,
            WindowsSessionId = sessionId,
            InstanceName = instanceName
        };
    }
}
