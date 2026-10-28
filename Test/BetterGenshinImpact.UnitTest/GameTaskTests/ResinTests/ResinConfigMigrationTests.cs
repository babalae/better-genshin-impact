using System.Reflection;
using System.Text.Json;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.ResinTests;

/// <summary>验证全局配置旧 JSON 的迁移、新配置默认值及迁移幂等性。</summary>
public sealed class ResinConfigMigrationTests
{

    /// <summary>仅包含旧字段的全局配置经读取迁移后，档位、目标及补充开关符合旧字段映射。</summary>
    [Fact]
    public void LegacyAllConfigJson_MigratesEveryTask()
    {
        const string json = """
        {
          "autoDomainConfig": { "specifyResinUse": true, "originalResinUseCount": 0,
            "originalResin20UseCount": 0, "originalResin40UseCount": 3, "condensedResinUseCount": 2 },
          "autoStygianOnslaughtConfig": { "specifyResinUse": true, "originalResinUseCount": 4,
            "condensedResinUseCount": 2, "transientResinUseCount": 1, "fragileResinUseCount": 3 },
          "autoBossConfig": { "useTransientResin": true, "useFragileResin": true,
            "specifyRunCount": false, "runCount": 6 },
          "autoLeyLineOutcropConfig": { "isResinExhaustionMode": true, "openModeCountMin": true,
            "count": 9, "useTransientResin": true, "useFragileResin": false }
        }
        """;
        var config = JsonSerializer.Deserialize<AllConfig>(json, ConfigService.JsonOptions)!;
        MigrateReadResult(config);
        Assert.Equal(ResinUsageLegacyMapper.FromDomain(true, 0, 2, 0, 0, 0, 3, out _), config.AutoDomainConfig.ResinUsage);
        Assert.Equal(ResinUsageLegacyMapper.FromStygian(true, 4, 2, 1, 3), config.AutoStygianOnslaughtConfig.ResinUsage);
        Assert.Equal(ResinUsageLegacyMapper.FromBoss(false, 6, true, true), config.AutoBossConfig.ResinUsage);
        Assert.Equal(ResinUsageLegacyMapper.FromLeyLine(true, true, 9, true, false), config.AutoLeyLineOutcropConfig.ResinUsage);
        var restored = JsonSerializer.Deserialize<AllConfig>(JsonSerializer.Serialize(config, ConfigService.JsonOptions), ConfigService.JsonOptions)!;
        MigrateReadResult(restored);
        Assert.Equal(config.AutoDomainConfig.ResinUsage, restored.AutoDomainConfig.ResinUsage);
        Assert.Equal(config.AutoBossConfig.ResinUsage, restored.AutoBossConfig.ResinUsage);
    }

    /// <summary>已有统一设置优先于旧字段，重复迁移不替换其引用。</summary>
    [Fact]
    public void Migration_PreservesNewValuesAndIsIdempotent()
    {
        var config = JsonSerializer.Deserialize<AllConfig>("""
        { "autoDomainConfig": { "specifyResinUse": true, "originalResinUseCount": 99,
            "resinUsage": { "mode": "RunCount", "runCount": 6,
              "allowed": { "original": true }, "originalTier": "Only20" } } }
        """, ConfigService.JsonOptions)!;
        var usage = config.AutoDomainConfig.ResinUsage!;
        MigrateReadResult(config);
        Assert.Same(usage, config.AutoDomainConfig.EnsureResinUsage());
        Assert.Equal(ResinUsageMode.RunCount, usage.Mode);
        Assert.Equal(6, usage.RunCount);
        Assert.Equal(OriginalResinTier.Only20, usage.OriginalTier);
        config.AutoDomainConfig.OriginalResinUseCount = 88;
        config.AutoDomainConfig.MigrateLegacyResinUsage();
        Assert.Same(usage, config.AutoDomainConfig.ResinUsage);
        Assert.Equal(0, usage.Counts.Original);
    }

    /// <summary>未经配置服务创建的配置可惰性迁移，非指定模式隐藏的混合档位不影响当前档位。</summary>
    [Fact]
    public void EnsureResinUsage_MigratesOnDemandAndIgnoresInactiveTier()
    {
        var domain = new AutoDomainConfig { SpecifyResinUse = false, OriginalResinUseCount = 4,
            OriginalResin20UseCount = 2, OriginalResin40UseCount = 3 };
        var usage = domain.EnsureResinUsage();
        Assert.Equal(9, usage.Counts.Original);
        Assert.Equal(OriginalResinTier.Default, usage.OriginalTier);
        Assert.Same(usage, domain.EnsureResinUsage());
        domain.ResinUsage = null;
        domain.SpecifyResinUse = true;
        Assert.Equal(OriginalResinTier.Default, domain.EnsureResinUsage().OriginalTier);
        Assert.Equal(9, domain.ResinUsage!.Counts.Original);
    }

    /// <summary>首领旧补充开关在关闭指定次数后仍保留，原有次数下限联动继续生效。</summary>
    [Fact]
    public void BossConfig_DoesNotClearSupplementSwitches()
    {
        var config = new AutoBossConfig { SpecifyRunCount = true, UseTransientResin = true, UseFragileResin = true };
        config.SpecifyRunCount = false;
        Assert.True(config.UseTransientResin);
        Assert.True(config.UseFragileResin);
        config.RunCount = 0;
        Assert.Equal(1, config.RunCount);
        var usage = config.EnsureResinUsage();
        Assert.Equal(ResinUsageMode.Exhaust, usage.Mode);
        Assert.True(usage.Allowed.Transient);
        Assert.True(usage.Allowed.Fragile);
    }

    /// <summary>调用 Read 各返回分支共用的迁移函数，不读取或写入实际用户配置。</summary>
    private static void MigrateReadResult(AllConfig config)
    {
        var migrate = typeof(ConfigService).GetMethod("MigrateLegacyConfig", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Same(config, migrate.Invoke(null, [config]));
    }
}
