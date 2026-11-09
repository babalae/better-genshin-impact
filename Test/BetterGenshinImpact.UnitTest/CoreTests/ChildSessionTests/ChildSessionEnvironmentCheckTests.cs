using System;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Service.ChildSession;

namespace BetterGenshinImpact.UnitTest.CoreTests.ChildSessionTests;

/// <summary>
/// 桌面分身环境预检的结构性测试。
///
/// 这些结论依机器而异，所以只断言「结构与判定规则」，不断言某台机器一定会检出什么：
/// 换一台干净的机器照样能跑过，检出问题时也不会失败。
/// </summary>
public class ChildSessionEnvironmentCheckTests
{
    [Fact]
    public void CollectAll_ShouldCoverEveryCheckWithUniqueId()
    {
        var all = ChildSessionEnvironmentCheck.CollectAll();

        Assert.NotEmpty(all);
        Assert.Equal(all.Count, all.Select(issue => issue.Id).Distinct().Count());

        // 新增/删除检查项时这里会直接红，比只对个数更容易看出漏了谁。
        string[] expectedIds =
        [
            "child-session-api-missing",
            "termservice-disabled",
            "termservice-stopped",
            "rdp-disabled",
            "hello-only",
            "rdp-wrapper",
            "termservice-dll-type",
            "termservice-key-broken",
            "rdp-tool-persistence",
            "termsrv-dll-tampered"
        ];

        var actualIds = all.Select(issue => issue.Id).ToHashSet(StringComparer.Ordinal);
        Assert.True(
            actualIds.SetEquals(expectedIds),
            "检查项集合与预期不一致。多出来：" + string.Join("、", actualIds.Except(expectedIds))
            + "；缺了：" + string.Join("、", expectedIds.Except(actualIds)));

        foreach (var issue in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(issue.Id));
            Assert.False(string.IsNullOrWhiteSpace(issue.Title));
            Assert.False(string.IsNullOrWhiteSpace(issue.Summary));
            Assert.False(string.IsNullOrWhiteSpace(issue.Detail));
        }
    }

    [Fact]
    public void Collect_ShouldOnlyReturnTriggeredIssues()
    {
        var triggered = ChildSessionEnvironmentCheck.Collect();
        var all = ChildSessionEnvironmentCheck.CollectAll();

        // 触发项必须是全部检查项的子集，且不能带 Normal 严重度。
        foreach (var issue in triggered)
        {
            Assert.NotEqual(ChildSessionIssueSeverity.Normal, issue.Severity);
            Assert.Contains(all, candidate => candidate.Id == issue.Id);
        }
    }

    [Fact]
    public void UntriggeredIssues_ShouldNotOfferAutoFix()
    {
        var all = ChildSessionEnvironmentCheck.CollectAll();

        foreach (var issue in all.Where(issue => issue.Severity == ChildSessionIssueSeverity.Normal))
        {
            // 未触发就没有可修的东西，给按钮会被误读成「这里有毛病」。
            Assert.False(issue.CanAutoFix);
            Assert.Contains("未触发", issue.Summary);
        }
    }

    [Fact]
    public void Collect_ShouldNotThrowWhenEnvironmentIsUnusual()
    {
        // 预检出现在启动失败路径上，它自己抛异常会把用户引向更糟的方向。
        var exception = Record.Exception(() => ChildSessionEnvironmentCheck.Collect());
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsServiceDllHijacked_ShouldIgnoreEmptyValues(string? serviceDll, bool expected)
    {
        // 空值交给「ServiceDll 缺失」那条结论，这里不算劫持。
        Assert.Equal(expected, ChildSessionEnvironmentCheck.IsServiceDllHijacked(serviceDll));
    }

    [Fact]
    public void IsServiceDllHijacked_ShouldAcceptNativePath()
    {
        Assert.False(ChildSessionEnvironmentCheck.IsServiceDllHijacked(
            ChildSessionEnvironmentCheck.GetNativeServiceDllPath()));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\superrdp.dll")]
    [InlineData(@"C:\Windows\System32\rdpwrap.dll")]
    [InlineData(@"%SystemRoot%\System32\RDPWrappers.dll")]
    public void IsServiceDllHijacked_ShouldReportForeignDllByName(string serviceDll)
    {
        // 判定按「是否等于原生路径」，所以改名版本（superrdp 等）同样能被抓到。
        Assert.True(ChildSessionEnvironmentCheck.IsServiceDllHijacked(serviceDll));
    }

    [Fact]
    public void IsServiceDllHijacked_ShouldTreatMalformedPathAsSuspicious()
    {
        // 路径畸形时不能静默放行。
        Assert.True(ChildSessionEnvironmentCheck.IsServiceDllHijacked("::not-a-path::"));
    }

    [Fact]
    public void QueryTermService_ShouldReturnKnownState()
    {
        var (state, startType) = ChildSessionEnvironmentCheck.QueryTermService();

        Assert.Contains(state, new[] { "Stopped", "StartPending", "StopPending", "Running", "Unknown" });
        Assert.Contains(startType, new[] { "Automatic", "Manual", "Disabled", "Unknown" });
    }

    [Fact]
    public void BuildSummary_ShouldBeEmptyWithoutIssues()
    {
        Assert.Equal(string.Empty, ChildSessionEnvironmentCheck.BuildSummary([]));
    }

    [Fact]
    public void BuildSummary_ShouldContainTitleAndSummary()
    {
        var issue = new ChildSessionEnvironmentCheck.Issue(
            "sample",
            "示例问题",
            "一句话结论。",
            "详细说明。",
            ChildSessionIssueSeverity.Warning,
            CanAutoFix: false);

        var text = ChildSessionEnvironmentCheck.BuildSummary([issue]);

        Assert.Contains("示例问题", text);
        Assert.Contains("一句话结论。", text);
    }
}
