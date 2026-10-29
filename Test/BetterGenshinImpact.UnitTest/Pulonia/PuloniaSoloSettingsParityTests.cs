using BetterGenshinImpact.GameTask.Common.Resin;
using System.Runtime.CompilerServices;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>新旧设置的参数保存和首领累计行为回归，不启动应用或游戏。</summary>
public sealed class PuloniaSoloSettingsParityTests
{

    /// <summary>累计上限与单次上限共同生效；耗尽方式转为指定次数后保留补充开关，且不修改共享原值。</summary>
    [Theory]
    [InlineData(false, 1, 10, 7, true, 3)]
    [InlineData(true, 2, 10, 7, true, 2)]
    [InlineData(true, 5, 10, 7, true, 3)]
    [InlineData(true, 5, 10, 10, false, 5)]
    [InlineData(true, 5, 0, 10, true, 5)]
    public void BossLimit_RespectsRemainingAndPerRunLimits(bool specified, int runCount, int limit,
        int completed, bool expectedRun, int expectedCount)
    {
        // 此方法只操作次数字段，绕过依赖正在运行应用的语言服务构造函数。
        var boss = (AutoBossParam)RuntimeHelpers.GetUninitializedObject(typeof(AutoBossParam));
        boss.ResinUsage = ResinCapabilities.Boss.Defaults;
        boss.SpecifyRunCount = specified;
        boss.RunCount = runCount;
        boss.UseTransientResin = true;
        boss.UseFragileResin = true;
        var shared = boss.ResinUsage;
        var snapshot = shared.Clone();
        Assert.Equal(expectedRun, PuloniaSoloTaskExecutor.ApplyBossRewardLimit(boss, limit, completed));
        Assert.Equal(snapshot, shared);
        Assert.Equal(expectedCount, boss.RunCount);
        Assert.True(boss.UseTransientResin);
        Assert.True(boss.UseFragileResin);
        Assert.Equal(expectedRun && limit > 0 ? ResinUsageMode.RunCount : snapshot.Mode, boss.ResinUsage.Mode);

    }

    /// <summary>累计跨存储实例持久化，并按任务、账号与清空批次分别统计；清空仅生成草稿。</summary>
    [Fact]
    public async Task BossProgress_PersistsAndSeparatesTasksAccountsAndResetDrafts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));
        try
        {
            using (var store = new PuloniaTaskStore(directory))
            {
                await store.IncrementBossRewardCountAsync("plan", "task", "", "account-a");
                await store.IncrementBossRewardCountAsync("plan", "task", "", "account-a");
                await store.IncrementBossRewardCountAsync("plan", "task", "", "account-b");
            }
            using var reopened = new PuloniaTaskStore(directory);
            var counts = await reopened.ReadBossRewardCountsAsync("plan", "task", "");
            Assert.Equal(2, counts["account-a"]);
            Assert.Equal(1, counts["account-b"]);
            Assert.Empty(await reopened.ReadBossRewardCountsAsync("plan", "other-task", ""));
            Assert.Empty(await reopened.ReadBossRewardCountsAsync("plan", "task", "new-generation"));
            var settings = new AutoBossSettingsViewModel(new JObject(), reopened);
            await settings.SetProgressScopeAsync("plan", "task");
            Assert.Contains("account-a：2 次", settings.CompletedRunCountText);
            settings.ResetProgressCommand.Execute(null);
            var changes = new JObject();
            settings.CollectChanges(changes);
            Assert.NotEmpty(changes.Value<string>(PuloniaSoloTaskExecutor.BossRewardCountResetParameter)!);
            Assert.Equal(2, (await reopened.ReadBossRewardCountsAsync("plan", "task", ""))["account-a"]);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
