using System.Text.Json;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.ResinTests;

/// <summary>验证树脂值对象、序列化合同、能力声明及各方式的校验边界。</summary>
public sealed class ResinUsageTests
{
    /// <summary>Schema 允许的嵌套 JSON null 回退为各自默认值，不破坏值相等、校验和深拷贝。</summary>
    [Theory]
    [InlineData("{\"allowed\":null,\"counts\":null}")]
    [InlineData("{\"counts\":null,\"allowed\":null}")]
    public void NullNestedValues_FallBackToIndependentDefaults(string json)
    {
        var usage = SoloTaskJson.Deserialize<ResinUsage>(json);
        Assert.Equal(new ResinUsage(), usage);
        Assert.Null(usage.Validate(ResinCapabilities.Domain));
        Assert.Equal(usage, SoloTaskJson.Deserialize<ResinUsage>(SoloTaskJson.Serialize(usage)));
        var clone = usage.Clone();
        clone.Allowed.Original = false;
        clone.Counts.Original = 3;
        Assert.True(usage.Allowed.Original);
        Assert.Equal(0, usage.Counts.Original);
        usage.Allowed = null;
        usage.Counts = null;
        Assert.Equal(new ResinUsage(), usage);
    }

    /// <summary>独立任务按 snake_case 和枚举名称往返，索引器不出现在 JSON 中。</summary>
    [Fact]
    public void SoloTaskJson_RoundTrip_PreservesNestedValuesAndContract()
    {
        var source = new ResinUsage
        {
            Mode = ResinUsageMode.PerType,
            RunCount = 6,
            Counts = new() { Condensed = 2, Original = 4, Transient = 1, Fragile = 3 },
            OriginalTier = OriginalResinTier.Prefer40
        };
        var json = SoloTaskJson.Serialize(source);
        var obj = JObject.Parse(json);
        Assert.Equal(new[] { "mode", "run_count", "allowed", "counts", "original_tier" }, obj.Properties().Select(p => p.Name));
        Assert.Equal("PerType", (string?)obj["mode"]);
        Assert.Equal(6, (int?)obj["run_count"]);
        Assert.Equal("Prefer40", (string?)obj["original_tier"]);
        Assert.True((bool)obj["allowed"]!["condensed"]!);
        foreach (var name in new[] { "allowed", "counts" })
            Assert.Equal(new[] { "condensed", "original", "transient", "fragile" }, ((JObject)obj[name]!).Properties().Select(p => p.Name));
        Assert.Equal(source, SoloTaskJson.Deserialize<ResinUsage>(json));
    }

    /// <summary>不依赖 SoloTaskJson 的枚举转换器时，五种枚举仍由类型属性按名称保存。</summary>
    [Fact]
    public void EnumAttributes_AlsoApplyToAllConfigOptions()
    {
        Assert.Equal("\"Condensed\"", JsonSerializer.Serialize(ResinKind.Condensed));
        Assert.Equal("\"RunCount\"", JsonSerializer.Serialize(ResinUsageMode.RunCount));
        Assert.Equal("\"Only20\"", JsonSerializer.Serialize(OriginalResinTier.Only20));
        Assert.Equal("\"Replenish\"", JsonSerializer.Serialize(ResinKindSupport.Replenish));
        Assert.Equal("\"NoUsableOption\"", JsonSerializer.Serialize(ResinStopReason.NoUsableOption));
        var source = new ResinUsage { Mode = ResinUsageMode.RunCount, OriginalTier = OriginalResinTier.Only20 };
        var json = JsonSerializer.Serialize(source, ConfigService.JsonOptions);
        Assert.Equal("RunCount", (string?)JObject.Parse(json)["mode"]);
        Assert.Equal("Only20", (string?)JObject.Parse(json)["originalTier"]);
        Assert.Equal(source, JsonSerializer.Deserialize<ResinUsage>(json, ConfigService.JsonOptions));
    }

    /// <summary>嵌套 record 按值比较，深拷贝可独立修改两组嵌套属性。</summary>
    [Fact]
    public void Clone_IsStructurallyEqualAndDeeplyIndependent()
    {
        var source = new ResinUsage { Counts = new() { Original = 4 } };
        var equivalent = new ResinUsage { Counts = new() { Original = 4 } };
        var clone = source.Clone();
        Assert.Equal(source, equivalent);
        Assert.Equal(source.GetHashCode(), equivalent.GetHashCode());
        Assert.Equal(source, clone);
        Assert.NotSame(source, clone);
        Assert.NotSame(source.Allowed, clone.Allowed);
        Assert.NotSame(source.Counts, clone.Counts);
        clone.Allowed.Condensed = false;
        clone.Counts.Original = 9;
        Assert.True(source.Allowed.Condensed);
        Assert.Equal(4, source.Counts.Original);
        Assert.NotEqual(source, clone);
    }

    /// <summary>四类值的索引器与声明顺序一致，并能独立复制。</summary>
    [Fact]
    public void KindValues_Indexer_FollowsFixedOrder()
    {
        var values = new ResinKindValues<int>();
        var next = 1;
        foreach (var kind in ResinKinds.InOrder)
            values[kind] = next++;
        Assert.Equal(new[] { 1, 2, 3, 4 }, new[] { values.Condensed, values.Original, values.Transient, values.Fragile });
        var clone = values.Clone();
        clone[ResinKind.Fragile] = 8;
        Assert.Equal(4, values.Fragile);
        Assert.Throws<ArgumentOutOfRangeException>(() => values[(ResinKind)99]);
        Assert.Throws<ArgumentOutOfRangeException>(() => values[(ResinKind)99] = 1);
    }

    /// <summary>四个能力声明的支持方式、默认设置和默认副本相互隔离。</summary>
    [Theory]
    [InlineData("Domain")]
    [InlineData("Stygian")]
    [InlineData("LeyLine")]
    [InlineData("Boss")]
    public void Capabilities_DeclareExpectedRulesAndIndependentDefaults(string name)
    {
        var capability = Capability(name);
        var defaults = capability.Defaults;
        Assert.Null(defaults.Validate(capability));
        Assert.Equal(name == "LeyLine" ? ResinUsageMode.RunCount : ResinUsageMode.Exhaust, defaults.Mode);
        Assert.Equal(name == "LeyLine" ? 6 : 1, defaults.RunCount);
        Assert.Equal(name == "Boss" ? 2 : 3, capability.Modes.Count);
        Assert.Equal(name == "Boss" ? 1 : 3, capability.Tiers.Count);
        Assert.Equal(name == "Boss" ? ResinKind.Original : (ResinKind?)null, capability.LockedKind);
        Assert.Equal(name != "Boss", defaults.Allowed.Condensed);
        Assert.True(defaults.Allowed.Original);
        Assert.False(defaults.Allowed.Transient);
        Assert.False(defaults.Allowed.Fragile);
        foreach (var kind in ResinKinds.InOrder)
        {
            var expected = name != "Boss" ? ResinKindSupport.Direct : kind switch
            {
                ResinKind.Condensed => ResinKindSupport.Unsupported,
                ResinKind.Original => ResinKindSupport.Direct,
                _ => ResinKindSupport.Replenish
            };
            Assert.Equal(expected, capability.Kinds[kind]);
            Assert.Equal(expected != ResinKindSupport.Unsupported, capability.IsSupported(kind));
        }
        defaults.Allowed.Original = false;
        defaults.Counts.Fragile = 9;
        Assert.True(capability.Defaults.Allowed.Original);
        Assert.Equal(0, capability.Defaults.Counts.Fragile);
        Assert.Equal(name == "Boss" ? "首领固定使用 40 原粹树脂" : name == "LeyLine"
            ? "双倍活动时切到 40，其余使用界面当前档位" : "默认使用领奖界面当前档位", capability.Hints.DefaultTierDescription);
        Assert.Equal("首领讨伐暂不支持浓缩树脂", ResinCapabilities.Boss.Hints.UnsupportedReasons.Condensed);
        Assert.Equal("首领讨伐只按领奖次数控制", ResinCapabilities.Boss.Hints.PerTypeUnsupportedReason);
    }

    /// <summary>能力初始化时也复制默认值，原始实例的修改不会污染能力。</summary>
    [Fact]
    public void Defaults_CopyOnInitialization()
    {
        var original = new ResinUsage();
        var capability = new ResinUsageCapability
        {
            Modes = ResinCapabilities.Domain.Modes, Kinds = ResinCapabilities.Domain.Kinds.Clone(),
            Tiers = ResinCapabilities.Domain.Tiers, Defaults = original
        };
        original.Allowed.Condensed = false;
        Assert.True(capability.Defaults.Allowed.Condensed);
    }

    /// <summary>总次数的上下边界只在指定次数方式生效，其他方式不读取它。</summary>
    [Theory]
    [InlineData("Domain")]
    [InlineData("Stygian")]
    [InlineData("LeyLine")]
    [InlineData("Boss")]
    public void Validate_RunCountBoundsAndInactiveFields(string name)
    {
        var capability = Capability(name);
        var usage = capability.Defaults;
        usage.Mode = ResinUsageMode.RunCount;
        usage.Counts = new() { Condensed = -1, Original = 1000 };
        foreach (var count in new[] { 1, 999 })
        {
            usage.RunCount = count;
            Assert.Null(usage.Validate(capability));
        }
        foreach (var count in new[] { -1, 0, 1000 })
        {
            usage.RunCount = count;
            Assert.Equal(count == 0 ? ResinUsageText.MissingCount : "领奖次数须为 1–999。", usage.Validate(capability));
            usage.Mode = ResinUsageMode.Exhaust;
            Assert.Null(usage.Validate(capability));
            usage.Mode = ResinUsageMode.RunCount;
        }
        usage.Mode = (ResinUsageMode)99;
        Assert.NotNull(usage.Validate(capability));
        usage.Mode = ResinUsageMode.Exhaust;
        usage.OriginalTier = (OriginalResinTier)99;
        Assert.NotNull(usage.Validate(capability));
    }

    /// <summary>全部关闭无效；耗尽方式可以开启须臾、脆弱，首领原粹仍必须开启。</summary>
    [Theory]
    [InlineData("Domain")]
    [InlineData("Stygian")]
    [InlineData("LeyLine")]
    [InlineData("Boss")]
    public void Validate_AllowedKindsAndExhaustReplenishment(string name)
    {
        var capability = Capability(name);
        var usage = capability.Defaults;
        usage.Mode = ResinUsageMode.Exhaust;
        usage.Allowed.Transient = true;
        usage.Allowed.Fragile = true;
        Assert.Null(usage.Validate(capability));
        usage.Allowed = new();
        Assert.NotNull(usage.Validate(capability));
        if (name != "Boss")
            Assert.Equal("尚未选择可用树脂", usage.Validate(capability));
        else
        {
            usage.Allowed.Transient = true;
            Assert.Equal("此任务必须开启原粹树脂。", usage.Validate(capability));
            usage.Allowed.Original = true;
            usage.Allowed.Condensed = true;
            Assert.Equal("此任务不能使用浓缩树脂。", usage.Validate(capability));
            usage.Allowed.Condensed = false;
            usage.OriginalTier = OriginalResinTier.Prefer40;
            Assert.NotNull(usage.Validate(capability));
            usage.OriginalTier = OriginalResinTier.Only20;
            Assert.NotNull(usage.Validate(capability));
            usage.OriginalTier = OriginalResinTier.Default;
            usage.Mode = ResinUsageMode.PerType;
            usage.Counts.Original = 1;
            Assert.NotNull(usage.Validate(capability));
        }
    }

    /// <summary>分别指定校验每类 0–999 和非空目标，忽略开关及总次数，并接受三种档位。</summary>
    [Theory]
    [InlineData("Domain")]
    [InlineData("Stygian")]
    [InlineData("LeyLine")]
    public void Validate_PerTypeBoundsAndInactiveFields(string name)
    {
        var capability = Capability(name);
        var usage = new ResinUsage { Mode = ResinUsageMode.PerType, RunCount = 1000, Allowed = new() };
        Assert.Equal("尚未填写次数", usage.Validate(capability));
        foreach (var kind in ResinKinds.InOrder)
        {
            usage.Counts = new();
            usage.Counts[kind] = 999;
            foreach (var tier in capability.Tiers)
            {
                usage.OriginalTier = tier;
                Assert.Null(usage.Validate(capability));
            }
            usage.Counts[kind] = 1;
            Assert.Null(usage.Validate(capability));
            usage.Counts[kind] = -1;
            Assert.NotNull(usage.Validate(capability));
            usage.Counts[kind] = 1000;
            Assert.NotNull(usage.Validate(capability));
        }
    }

    /// <summary>分别指定时不支持的种类必须为 0，隐藏的 allowed 不参与校验。</summary>
    [Fact]
    public void Validate_PerTypeUnsupportedCounts()
    {
        var capability = new ResinUsageCapability
        {
            Modes = ResinCapabilities.Domain.Modes, Kinds = new() { Original = ResinKindSupport.Direct },
            Tiers = ResinCapabilities.Domain.Tiers, Defaults = new ResinUsage()
        };
        var usage = new ResinUsage { Mode = ResinUsageMode.PerType, Counts = new() { Original = 1 } };
        Assert.Null(usage.Validate(capability));
        usage.Counts.Condensed = 1;
        Assert.Equal("此任务不能使用浓缩树脂，领奖次数须为 0。", usage.Validate(capability));
    }

    /// <summary>现有 Schema 生成器可直接展开嵌套对象，并禁止未知属性。</summary>
    [Fact]
    public void Schema_GeneratesStrictNestedObjectsWithoutIndexer()
    {
        var schema = PuloniaSoloTaskSchema.Create(typeof(ResinTestParam));
        var usage = schema["properties"]!["resin_usage"]!;
        Assert.False((bool)schema["additionalProperties"]!);
        Assert.False((bool)usage["additionalProperties"]!);
        Assert.Equal(new[] { "Exhaust", "RunCount", "PerType" }, usage["properties"]!["mode"]!["enum"]!.Values<string>());
        foreach (var name in new[] { "allowed", "counts" })
        {
            var nested = usage["properties"]![name]!;
            Assert.False((bool)nested["additionalProperties"]!);
            Assert.Equal(4, ((JObject)nested["properties"]!).Count);
        }
    }

    /// <summary>按测试数据名称取得对应任务能力。</summary>
    private static ResinUsageCapability Capability(string name) => name switch
    {
        "Domain" => ResinCapabilities.Domain,
        "Stygian" => ResinCapabilities.StygianOnslaught,
        "LeyLine" => ResinCapabilities.LeyLineOutcrop,
        "Boss" => ResinCapabilities.Boss,
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    /// <summary>仅承载公共模型的测试参数，不修改实际任务参数或 Pulonia 代码。</summary>
    public sealed class ResinTestParam
    {
        /// <summary>测试用树脂参数字段。</summary>
        public ResinUsage ResinUsage = new();
    }
}
