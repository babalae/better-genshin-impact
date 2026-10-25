using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.ResinTests;

/// <summary>验证参数 JSON 合同、JS 兼容投影和组合赋值归一化。</summary>
public sealed class ResinParameterTests
{
    /// <summary>旧一条龙只写模式开关而未写 Count 时，不将分别指定覆盖成陈旧总次数。</summary>
    [Theory]
    [InlineData(false, false, ResinUsageMode.PerType)]
    [InlineData(false, true, ResinUsageMode.PerType)]
    [InlineData(true, true, ResinUsageMode.PerType)]
    [InlineData(true, false, ResinUsageMode.Exhaust)]
    public void LeyLine_LegacySwitchesPreservePerTypeWithoutExplicitCount(bool exhaustion, bool min, ResinUsageMode expected)
    {
        var param = ResinParameterTestFactory.LeyLine();
        param.ResinUsage = new() { Mode = ResinUsageMode.PerType, RunCount = 99, Counts = new() { Condensed = 2, Original = 4 } };
        param.IsResinExhaustionMode = exhaustion;
        param.OpenModeCountMin = min;
        param.ResolveResinUsage();
        Assert.Equal(expected, param.ResinUsage.Mode);
        Assert.Equal(99, param.ResinUsage.RunCount);
        Assert.Equal(new ResinKindValues<int> { Condensed = 2, Original = 4 }, param.ResinUsage.Counts);
        param.IsResinExhaustionMode = false;
        param.Count = 6;
        param.ResolveResinUsage();
        Assert.Equal(ResinUsageMode.RunCount, param.ResinUsage.Mode);
        Assert.Equal(6, param.ResinUsage.RunCount);
    }

    /// <summary>四个参数的 JSON 和 Schema 只暴露统一树脂入口，旧属性均忽略。</summary>
    [Theory]
    [InlineData("Domain")]
    [InlineData("Stygian")]
    [InlineData("Boss")]
    [InlineData("LeyLine")]
    public void Parameters_ExcludeEveryLegacyResinKey(string name)
    {
        object param = name switch
        {
            "Domain" => ResinParameterTestFactory.Domain(), "Stygian" => ResinParameterTestFactory.Stygian(),
            "Boss" => ResinParameterTestFactory.Boss(), _ => ResinParameterTestFactory.LeyLine()
        };
        var json = JObject.Parse(SoloTaskJson.Serialize(param, param.GetType()));
        var properties = (JObject)PuloniaSoloTaskSchema.Create(param.GetType())["properties"]!;
        Assert.IsType<JObject>(json["resin_usage"]);
        Assert.IsType<JObject>(properties["resin_usage"]);
        foreach (var key in new[]
        {
            "specify_resin_use", "resin_priority_list", "original_resin_use_count", "original_resin20_use_count",
            "original_resin40_use_count", "condensed_resin_use_count", "transient_resin_use_count", "fragile_resin_use_count",
            "domain_round_num", "specify_run_count", "run_count", "use_transient_resin", "use_fragile_resin",
            "count", "open_mode_count_min", "is_resin_exhaustion_mode", "is_go_to_synthesizer"
        })
        {
            Assert.False(json.ContainsKey(key), key);
            Assert.False(properties.ContainsKey(key), key);
        }
    }

    /// <summary>秘境模式和原粹三个次数按全部 24 种顺序赋值，归一化结果相同。</summary>
    [Theory]
    [InlineData(0, 0, 3, 3, OriginalResinTier.Prefer40)]
    [InlineData(0, 2, 0, 2, OriginalResinTier.Only20)]
    [InlineData(0, 2, 3, 5, OriginalResinTier.Default)]
    [InlineData(4, 2, 0, 6, OriginalResinTier.Default)]
    [InlineData(4, 0, 3, 7, OriginalResinTier.Default)]
    [InlineData(4, 2, 3, 9, OriginalResinTier.Default)]
    public void Domain_LegacyAssignmentOrderDoesNotMatter(int original, int original20, int original40,
        int expected, OriginalResinTier tier)
    {
        foreach (var order in Orders(4))
        {
            var param = ResinParameterTestFactory.Domain();
            param.CondensedResinUseCount = 2;
            param.ResinUsage.Allowed.Transient = true;
            Action[] setters = [() => param.SpecifyResinUse = true, () => param.OriginalResinUseCount = original,
                () => param.OriginalResin20UseCount = original20, () => param.OriginalResin40UseCount = original40];
            foreach (var index in order) setters[index]();
            param.ResolveResinUsage();
            Assert.Equal(ResinUsageMode.PerType, param.ResinUsage.Mode);
            Assert.Equal(expected, param.ResinUsage.Counts.Original);
            Assert.Equal(tier, param.ResinUsage.OriginalTier);
            Assert.Equal(2, param.ResinUsage.Counts.Condensed);
            Assert.True(param.ResinUsage.Allowed.Transient);
            var snapshot = param.ResinUsage.Clone();
            param.ResolveResinUsage();
            Assert.Equal(snapshot, param.ResinUsage);
        }
    }

    /// <summary>秘境未指定时不切换档位；未编辑组合字段时保持新模型及其引用。</summary>
    [Fact]
    public void Domain_OnlyResolvesEditedOriginalGroupAndProjectsTierCounts()
    {
        var param = ResinParameterTestFactory.Domain();
        param.ResinUsage = new() { Mode = ResinUsageMode.RunCount, RunCount = 8,
            OriginalTier = OriginalResinTier.Only20, Counts = new() { Original = 4, Fragile = 2 } };
        var original = param.ResinUsage;
        param.ResolveResinUsage();
        Assert.Same(original, param.ResinUsage);
        Assert.Equal(0, param.OriginalResinUseCount);
        Assert.Equal(4, param.OriginalResin20UseCount);
        Assert.Equal(0, param.OriginalResin40UseCount);
        param.SpecifyResinUse = false;
        param.OriginalResin20UseCount = 3;
        Assert.Equal(3, param.OriginalResin20UseCount);
        param.ResolveResinUsage();
        Assert.Equal(OriginalResinTier.Default, param.ResinUsage.OriginalTier);
        Assert.Equal(3, param.ResinUsage.Counts.Original);
        Assert.Equal(2, param.ResinUsage.Counts.Fragile);
        Assert.Equal(8, param.ResinUsage.RunCount);
        param.ResinUsage.OriginalTier = OriginalResinTier.Prefer40;
        Assert.Equal(3, param.OriginalResin40UseCount);
    }

    /// <summary>地脉花四种开关组合按全部 120 种赋值顺序归一化，保留新模型其他字段。</summary>
    [Theory]
    [InlineData(false, false, ResinUsageMode.RunCount)]
    [InlineData(false, true, ResinUsageMode.RunCount)]
    [InlineData(true, false, ResinUsageMode.Exhaust)]
    [InlineData(true, true, ResinUsageMode.RunCount)]
    public void LeyLine_LegacyAssignmentOrderDoesNotMatter(bool exhaustion, bool min, ResinUsageMode expected)
    {
        foreach (var order in Orders(5))
        {
            var param = ResinParameterTestFactory.LeyLine();
            param.ResinUsage.Allowed.Condensed = false;
            param.ResinUsage.OriginalTier = OriginalResinTier.Only20;
            param.ResinUsage.Counts.Original = 7;
            Action[] setters = [() => param.IsResinExhaustionMode = exhaustion, () => param.OpenModeCountMin = min,
                () => param.Count = 9, () => param.UseTransientResin = true, () => param.UseFragileResin = true];
            foreach (var index in order) setters[index]();
            Assert.Equal(9, param.Count);
            param.ResolveResinUsage();
            Assert.Equal(expected, param.ResinUsage.Mode);
            Assert.Equal(9, param.ResinUsage.RunCount);
            Assert.True(param.ResinUsage.Allowed.Transient);
            Assert.True(param.ResinUsage.Allowed.Fragile);
            Assert.False(param.ResinUsage.Allowed.Condensed);
            Assert.Equal(OriginalResinTier.Only20, param.ResinUsage.OriginalTier);
            Assert.Equal(7, param.ResinUsage.Counts.Original);
            var snapshot = param.ResinUsage.Clone();
            param.ResolveResinUsage();
            Assert.Equal(snapshot, param.ResinUsage);
        }
    }

    /// <summary>地脉花未赋值时使用新模型投影；仅改补充开关不应覆盖分别指定方式或其他开关。</summary>
    [Fact]
    public void LeyLine_ProjectsNewModesAndOnlyUpdatesAssignedFields()
    {
        var param = ResinParameterTestFactory.LeyLine();
        param.ResinUsage = new() { Mode = ResinUsageMode.PerType, RunCount = 11, Counts = new() { Original = 4, Condensed = 2 } };
        var original = param.ResinUsage;
        param.ResolveResinUsage();
        Assert.Same(original, param.ResinUsage);
        Assert.Equal(6, param.Count);
        param.UseTransientResin = true;
        param.ResolveResinUsage();
        Assert.Equal(ResinUsageMode.PerType, param.ResinUsage.Mode);
        Assert.Equal(11, param.ResinUsage.RunCount);
        Assert.True(param.ResinUsage.Allowed.Transient);
        Assert.False(param.ResinUsage.Allowed.Fragile);
        param.ResinUsage.Mode = ResinUsageMode.Exhaust;
        Assert.Equal(11, param.Count);
        Assert.True(param.IsResinExhaustionMode);
        param.Count = 3; // 旧任务的预计算仍写 Count，setter 必须即时可读。
        Assert.Equal(3, param.Count);
        param.ResolveResinUsage();
        Assert.Equal(3, param.ResinUsage.RunCount);
        Assert.Equal(ResinUsageMode.Exhaust, param.ResinUsage.Mode);
    }

    /// <summary>直接兼容属性修改参数自己的嵌套值，关闭首领指定次数不清空补充开关。</summary>
    [Fact]
    public void DirectCompatibilityProperties_WriteThroughAndKeepSupplements()
    {
        var stygian = ResinParameterTestFactory.Stygian();
        stygian.SpecifyResinUse = true;
        stygian.OriginalResinUseCount = 4;
        stygian.CondensedResinUseCount = 2;
        stygian.TransientResinUseCount = 1;
        stygian.FragileResinUseCount = 3;
        Assert.Equal(new ResinKindValues<int> { Original = 4, Condensed = 2, Transient = 1, Fragile = 3 }, stygian.ResinUsage.Counts);
        Assert.Equal(ResinUsageMode.PerType, stygian.ResinUsage.Mode);
        var boss = ResinParameterTestFactory.Boss();
        boss.SpecifyRunCount = true;
        boss.RunCount = 6;
        boss.UseTransientResin = true;
        boss.UseFragileResin = true;
        boss.SpecifyRunCount = false;
        Assert.Equal(ResinUsageMode.Exhaust, boss.ResinUsage.Mode);
        Assert.Equal(6, boss.ResinUsage.RunCount);
        Assert.True(boss.ResinUsage.Allowed.Transient);
        Assert.True(boss.ResinUsage.Allowed.Fragile);
    }

    /// <summary>四类参数复制配置后，修改参数的次数或开关均不污染配置。</summary>
    [Fact]
    public void ConfigurationCopies_AreDeepAndLegacyListsAreNotShared()
    {
        var domainConfig = new AutoDomainConfig { SpecifyResinUse = true, OriginalResinUseCount = 4 };
        var domain = ResinParameterTestFactory.Domain();
        domain.SetAutoDomainConfig(domainConfig);
        domain.OriginalResinUseCount = 9;
        domain.ResolveResinUsage();
        domain.ResinUsage.Allowed.Original = false;
        domain.ResinPriorityList.Clear();
        Assert.Equal(4, domainConfig.EnsureResinUsage().Counts.Original);
        Assert.True(domainConfig.EnsureResinUsage().Allowed.Original);
        Assert.Equal(2, domainConfig.ResinPriorityList.Count);

        var stygianConfig = new AutoStygianOnslaughtConfig { StrategyName = "根据队伍自动选择", SpecifyResinUse = true, CondensedResinUseCount = 2 };
        var stygian = ResinParameterTestFactory.Stygian();
        stygian.SetAutoStygianOnslaughtConfig(stygianConfig);
        stygian.CondensedResinUseCount = 8;
        Assert.Equal(2, stygianConfig.EnsureResinUsage().Counts.Condensed);

        var bossConfig = new AutoBossConfig { SpecifyRunCount = true, RunCount = 6 };
        var boss = ResinParameterTestFactory.Boss();
        boss.SetAutoBossConfig(bossConfig);
        boss.RunCount = 9;
        boss.UseFragileResin = true;
        Assert.Equal(6, bossConfig.EnsureResinUsage().RunCount);
        Assert.False(bossConfig.EnsureResinUsage().Allowed.Fragile);

        var leyConfig = new AutoLeyLineOutcropConfig { Count = 6 };
        var ley = ResinParameterTestFactory.LeyLine();
        ley.SetAutoLeyLineOutcropConfig(leyConfig);
        ley.Count = 9;
        ley.UseTransientResin = true;
        ley.ResolveResinUsage();
        Assert.Equal(6, leyConfig.EnsureResinUsage().RunCount);
        Assert.False(leyConfig.EnsureResinUsage().Allowed.Transient);
    }

    /// <summary>任务的静态能力挂接到公共声明，不改变执行逻辑。</summary>
    [Fact]
    public void TaskCapabilities_ReferenceSharedDeclarations()
    {
        Assert.Same(ResinCapabilities.Domain, AutoDomainTask.ResinCapability);
        Assert.Same(ResinCapabilities.StygianOnslaught, AutoStygianOnslaughtTask.ResinCapability);
        Assert.Same(ResinCapabilities.Boss, AutoBossTask.ResinCapability);
        Assert.Same(ResinCapabilities.LeyLineOutcrop, AutoLeyLineOutcropTask.ResinCapability);
    }

    /// <summary>枚举指定数量赋值动作的全部排列，覆盖任意 setter 顺序。</summary>
    private static IEnumerable<int[]> Orders(int count)
    {
        if (count == 0) { yield return []; yield break; }
        foreach (var previous in Orders(count - 1))
            for (var i = 0; i < count; i++)
                yield return previous.Take(i).Append(count - 1).Concat(previous.Skip(i)).ToArray();
    }
}
