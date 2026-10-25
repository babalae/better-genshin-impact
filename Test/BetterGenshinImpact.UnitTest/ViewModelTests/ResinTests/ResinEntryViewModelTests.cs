using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.ViewModel.Resin;

namespace BetterGenshinImpact.UnitTest.ViewModelTests.ResinTests;

/// <summary>入口替换、取消和可编辑性测试。</summary>
public sealed class ResinEntryViewModelTests
{
    /// <summary>没有弹窗服务时命令不可执行，非法值仍能展示统一原因。</summary>
    [Fact]
    public void Entry_WithoutDialogsShowsProblemAndDisablesEdit()
    {
        var entry = new ResinUsageEntryViewModel("秘境", ResinCapabilities.Domain,
            new() { Mode = ResinUsageMode.PerType }, null);
        Assert.False(entry.CanEdit);
        Assert.False(entry.EditCommand.CanExecute(null));
        Assert.True(entry.HasProblem);
        Assert.Equal(ResinUsageText.MissingCount, entry.Problem);
        Assert.Equal(entry.Problem, entry.Summary);
    }

    /// <summary>入口持有副本，外部原值和返回值均不能原地修改入口；Reset 仅在变化时通知。</summary>
    [Fact]
    public void Reset_ClonesAndOnlyNotifiesOnChange()
    {
        var original = new ResinUsage();
        var entry = new ResinUsageEntryViewModel("秘境", ResinCapabilities.Domain, original, null);
        original.Allowed.Condensed = false;
        Assert.True(entry.Value.Allowed.Condensed);
        entry.Value.Allowed.Original = false;
        Assert.True(entry.Value.Allowed.Original);
        var changes = new List<string?>();
        entry.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        entry.Reset(entry.Value);
        Assert.Empty(changes);
        var next = new ResinUsage { Mode = ResinUsageMode.RunCount, RunCount = 6 };
        entry.Reset(next);
        Assert.Equal(new[] { "Value", "Summary", "Problem", "HasProblem" }, changes);
        Assert.Equal("领奖 6 次 · 浓缩 → 原粹", entry.Summary);
        Assert.False(entry.HasProblem);
        next.Allowed.Original = false;
        Assert.True(entry.Value.Allowed.Original);
    }

    /// <summary>取消和确认相同值不发出修改，实际变化才整体替换；传给弹窗的也是副本。</summary>
    [Fact]
    public void Edit_OnlyReplacesChangedConfirmedValues()
    {
        var dialogs = new DialogStub();
        var entry = new ResinUsageEntryViewModel("自动秘境", ResinCapabilities.Domain, new(), dialogs);
        var changes = 0;
        entry.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(entry.Value)) changes++; };
        Assert.True(entry.CanEdit);
        Assert.True(entry.EditCommand.CanExecute(null));
        dialogs.Edit = current => { current.Allowed.Original = false; return null; };
        entry.EditCommand.Execute(null);
        Assert.True(entry.Value.Allowed.Original);
        dialogs.Edit = current => current;
        entry.EditCommand.Execute(null);
        Assert.Equal(0, changes);
        dialogs.Edit = current => { current.RunCount = 6; current.Mode = ResinUsageMode.RunCount; return current; };
        entry.EditCommand.Execute(null);
        Assert.Equal(1, changes);
        Assert.Equal(6, entry.Value.RunCount);
        Assert.Equal("自动秘境", dialogs.TaskName);
    }

    /// <summary>可替换返回结果的弹窗替身。</summary>
    private sealed class DialogStub : IResinSettingsDialogs
    {
        /// <summary>当前编辑行为。</summary>
        public Func<ResinUsage, ResinUsage?> Edit { get; set; } = _ => null;
        /// <summary>最近调用的任务名。</summary>
        public string? TaskName { get; private set; }
        /// <inheritdoc />
        public ResinUsage? EditResinUsage(string taskName, ResinUsageCapability capability, ResinUsage current)
        {
            TaskName = taskName;
            return Edit(current);
        }
        /// <inheritdoc />
        public int? EditCraftResin(int minResinToKeep) => null;
    }
}
