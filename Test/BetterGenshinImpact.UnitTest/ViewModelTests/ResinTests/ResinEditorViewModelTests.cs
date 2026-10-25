using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.ViewModel.Resin;

namespace BetterGenshinImpact.UnitTest.ViewModelTests.ResinTests;

/// <summary>草稿、隐藏值、能力限制与异步确认测试，不创建 WPF 窗口。</summary>
public sealed class ResinEditorViewModelTests
{
    /// <summary>四种能力与三种方式的输入，包括不支持和隐藏字段，打开后均保持原值且不提示。</summary>
    [Fact]
    public void Open_PreservesAllValuesAndDoesNotConfirm()
    {
        foreach (var capability in new[] { ResinCapabilities.Domain, ResinCapabilities.StygianOnslaught, ResinCapabilities.LeyLineOutcrop, ResinCapabilities.Boss })
        foreach (var mode in Enum.GetValues<ResinUsageMode>())
        {
            var usage = capability.Defaults;
            usage.Mode = mode;
            usage.RunCount = 17;
            usage.Counts = new() { Condensed = 2, Original = 4, Transient = 3, Fragile = 1 };
            usage.Allowed.Transient = true;
            var calls = 0;
            var vm = new ResinUsageEditorViewModel("测试任务", capability, usage, _ => { calls++; return Task.FromResult(true); });
            Assert.Equal(usage, vm.BuildValue());
            Assert.Equal(0, calls);
            Assert.Equal("树脂使用 · 测试任务", vm.Title);
            Assert.Equal(ResinKinds.InOrder, vm.Kinds.Select(row => row.Kind));
            vm.Kinds[0].Count++;
            Assert.Equal(2, usage.Counts.Condensed);
        }
    }

    /// <summary>模式切换仅改变可见性与说明，开关、总次数、分类次数和档位均保留。</summary>
    [Fact]
    public void Modes_PreserveHiddenValuesAndUpdateDescriptions()
    {
        var usage = new ResinUsage { Mode = ResinUsageMode.RunCount, RunCount = 7, Counts = new() { Condensed = 2, Original = 4 } };
        var vm = Create(usage);
        Assert.True(vm.ShowRunCount);
        vm.IsPerTypeMode = true;
        Assert.False(vm.ShowRunCount);
        Assert.Equal("各树脂领奖次数", vm.KindsHeader);
        Assert.Equal("0 表示不使用此类树脂；某类不足时使用下一类", vm.KindsDescription);
        Assert.All(vm.Kinds, row => { Assert.True(row.ShowCount); Assert.False(row.ShowToggle); });
        Assert.Equal("浓缩完成后使用", vm.Kinds[1].Description);
        vm.Kinds[0].Count = 3;
        vm.SelectedTier = vm.TierChoices.Single(choice => choice.Tier == OriginalResinTier.Only20);
        vm.IsExhaustMode = true;
        Assert.All(vm.Kinds, row => { Assert.False(row.ShowCount); Assert.True(row.ShowToggle); });
        Assert.EndsWith("，直到用完", vm.Kinds[2].Description);
        Assert.Equal("可用树脂", vm.KindsHeader);
        Assert.Equal("按从上到下的顺序使用", vm.KindsDescription);
        vm.IsRunCountMode = true;
        Assert.Equal(7, vm.RunCount);
        Assert.Equal(3, vm.BuildValue().Counts.Condensed);
        Assert.Equal(OriginalResinTier.Only20, vm.BuildValue().OriginalTier);
        Assert.True(vm.BuildValue().Allowed.Original);
    }

    /// <summary>校验直接使用任务层文案；不相关的隐藏字段不参与校验。</summary>
    [Theory]
    [InlineData(ResinUsageMode.PerType, ResinUsageText.MissingCount)]
    [InlineData(ResinUsageMode.Exhaust, ResinUsageText.MissingAllowed)]
    [InlineData(ResinUsageMode.RunCount, ResinUsageText.MissingCount)]
    public void Validation_DisablesConfirmAndShowsSharedMessage(ResinUsageMode mode, string expected)
    {
        var usage = new ResinUsage { Mode = mode, RunCount = 0 };
        if (mode == ResinUsageMode.Exhaust) usage.Allowed = new();
        var vm = Create(usage);
        Assert.False(vm.CanConfirm);
        Assert.Equal(expected, vm.ValidationMessage);
        Assert.Equal(expected, vm.Summary);
    }

    /// <summary>首领只显示一个不可编辑档位，原粹锁定，浓缩和分别指定显示专属原因。</summary>
    [Fact]
    public void Boss_LocksOriginalAndDisablesUnsupportedOptions()
    {
        var vm = new ResinUsageEditorViewModel("自动首领讨伐", ResinCapabilities.Boss, ResinCapabilities.Boss.Defaults, _ => Task.FromResult(true));
        Assert.True(vm.CanConfirm);
        Assert.False(vm.IsPerTypeModeEnabled);
        Assert.Equal("首领讨伐只按领奖次数控制", vm.PerTypeModeDescription);
        Assert.False(vm.IsTierEditable);
        Assert.Single(vm.TierChoices);
        Assert.Equal("首领固定使用 40 原粹树脂", vm.TierDescription);
        Assert.False(vm.Kinds[0].IsEditable);
        Assert.Equal("首领讨伐暂不支持浓缩树脂", vm.Kinds[0].Description);
        Assert.True(vm.Kinds[1].IsAllowed);
        Assert.False(vm.Kinds[1].IsEditable);
        vm.Kinds[1].IsAllowed = false;
        Assert.True(vm.Kinds[1].IsAllowed);
        Assert.Equal("首领奖励固定消耗 40 原粹树脂", vm.Kinds[1].Description);
        Assert.Equal("原粹不足 40 时，战前补充为原粹树脂", vm.Kinds[2].Description);
    }

    /// <summary>耗尽开启补充树脂先显示开启，异步否决后回退一次；直接与战前补充正文不同。</summary>
    [Theory]
    [InlineData(false, ResinKind.Transient)]
    [InlineData(false, ResinKind.Fragile)]
    [InlineData(true, ResinKind.Transient)]
    [InlineData(true, ResinKind.Fragile)]
    public async Task Exhaust_EnableSupplement_ConfirmsAndRestoresOnNo(bool boss, ResinKind kind)
    {
        var capability = boss ? ResinCapabilities.Boss : ResinCapabilities.Domain;
        var response = new TaskCompletionSource<bool>();
        var requests = new List<ResinConfirmation>();
        var vm = new ResinUsageEditorViewModel("测试", capability, capability.Defaults, request => { requests.Add(request); return response.Task; });
        var row = vm.Kinds.Single(row => row.Kind == kind);
        row.IsAllowed = true;
        Assert.True(row.IsAllowed);
        Assert.False(vm.CanConfirm);
        var name = ResinUsageText.DisplayName(kind);
        var request = Assert.Single(requests);
        Assert.Equal("使用" + name, request.Title);
        Assert.Equal(boss ? $"刷至树脂耗尽时，原粹不足 40 会持续用{name}补充，直到用完。\n确定开启吗？"
            : $"刷至树脂耗尽时，浓缩和原粹不足后会持续使用{name}，直到用完。\n确定开启吗？", request.Message);
        response.SetResult(false);
        await vm.PendingConfirmation;
        Assert.False(row.IsAllowed);
        Assert.True(vm.CanConfirm);
        Assert.Single(requests);
    }

    /// <summary>切入耗尽只确认一次，否决关闭补充开关但保留耗尽方式。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchToExhaust_ConfirmsBothEnabledKinds(bool accepted)
    {
        var usage = new ResinUsage { Mode = ResinUsageMode.RunCount, Allowed = new() { Original = true, Transient = true, Fragile = true } };
        var requests = new List<ResinConfirmation>();
        var vm = new ResinUsageEditorViewModel("测试", ResinCapabilities.Domain, usage, request => { requests.Add(request); return Task.FromResult(accepted); });
        Assert.Empty(requests);
        vm.Mode = ResinUsageMode.Exhaust;
        await vm.PendingConfirmation;
        var request = Assert.Single(requests);
        Assert.Equal("刷至树脂耗尽", request.Title);
        Assert.Equal("已开启须臾树脂、脆弱树脂。刷至树脂耗尽会持续使用它们，直到用完。\n是否继续使用？", request.Message);
        Assert.Equal(ResinUsageMode.Exhaust, vm.Mode);
        Assert.Equal(accepted, vm.Kinds[2].IsAllowed);
        Assert.Equal(accepted, vm.Kinds[3].IsAllowed);
    }

    /// <summary>其他方式不提示，初始化已有的开启状态也不提示。</summary>
    [Theory]
    [InlineData(ResinUsageMode.RunCount)]
    [InlineData(ResinUsageMode.PerType)]
    [InlineData(ResinUsageMode.Exhaust)]
    public async Task Confirmations_DoNotRunDuringInitializationOrBoundedModes(ResinUsageMode mode)
    {
        var usage = new ResinUsage { Mode = mode };
        usage.Allowed.Transient = true;
        var calls = 0;
        var vm = new ResinUsageEditorViewModel("测试", ResinCapabilities.Domain, usage, _ => { calls++; return Task.FromResult(true); });
        Assert.Equal(0, calls);
        if (mode != ResinUsageMode.Exhaust) vm.Kinds[3].IsAllowed = true;
        await vm.PendingConfirmation;
        Assert.Equal(0, calls);
    }

    /// <summary>旧确认不会覆盖用户后续切换，新确认串行显示，否决恢复不会触发第三次确认。</summary>
    [Fact]
    public async Task Confirmation_RejectsStaleResultAndSuppressesReentry()
    {
        var firstResponse = new TaskCompletionSource<bool>();
        var secondResponse = new TaskCompletionSource<bool>();
        var calls = 0;
        var vm = new ResinUsageEditorViewModel("测试", ResinCapabilities.Domain, new(), _ => ++calls == 1 ? firstResponse.Task : secondResponse.Task);
        vm.Kinds[2].IsAllowed = true;
        var first = vm.PendingConfirmation;
        vm.Mode = ResinUsageMode.RunCount;
        vm.Mode = ResinUsageMode.Exhaust;
        Assert.Equal(1, calls);
        firstResponse.SetResult(false);
        await first;
        Assert.True(vm.Kinds[2].IsAllowed);
        secondResponse.SetResult(false);
        await vm.PendingConfirmation;
        Assert.False(vm.Kinds[2].IsAllowed);
        Assert.Equal(2, calls);
    }

    /// <summary>合成旧值归一化为默认，首次保留预填 40，隐藏数量与边界校验保持。</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Craft_DefaultPrefills40AndRetainsHiddenAmount(int legacy)
    {
        var vm = new CraftResinEditorViewModel(legacy);
        Assert.True(vm.IsDefaultMode);
        Assert.Equal(0, vm.BuildValue());
        Assert.Equal("按默认数量合成", vm.Summary);
        vm.KeepResin = true;
        Assert.Equal(40, vm.KeepAmount);
        Assert.Equal("合成后保留 40 原粹树脂", vm.Summary);
        vm.KeepAmount = 73;
        vm.IsDefaultMode = true;
        Assert.Equal(0, vm.BuildValue());
        vm.KeepResin = true;
        Assert.Equal(73, vm.BuildValue());
        vm.KeepAmount = 1000;
        Assert.False(vm.CanConfirm);
        Assert.Equal("保留数量须为 1–999。", vm.ValidationMessage);
        vm.KeepAmount = 999;
        Assert.True(vm.CanConfirm);
    }

    /// <summary>创建无需交互确认的秘境编辑器。</summary>
    private static ResinUsageEditorViewModel Create(ResinUsage usage)
        => new("自动秘境", ResinCapabilities.Domain, usage, _ => Task.FromResult(true));
}
