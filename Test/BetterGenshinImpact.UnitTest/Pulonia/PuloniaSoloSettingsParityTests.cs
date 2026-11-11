using BetterGenshinImpact.GameTask.Common.Resin;
using System.Runtime.CompilerServices;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
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

    /// <summary>
    /// 累计按账户存放在各账户的 boss-rewards.json，跨存储实例持久化，并按任务与清空批次分别统计；清空仅生成草稿。
    /// 新写入的条目带计划、节点与批次，键与旧文件名的哈希算法相同。
    /// </summary>
    [Fact]
    public async Task BossProgress_PersistsAndSeparatesTasksAccountsAndResetDrafts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));
        try
        {
            const string accountA = "acc_aaaaaaaaaaaaaaaa";
            const string accountB = "acc_bbbbbbbbbbbbbbbb";
            using (var store = new PuloniaTaskStore(directory))
            {
                var settingsFile = await store.LoadAccountSettingsAsync();
                settingsFile.Accounts = [new PuloniaGenshinAccountProfile { Id = accountA, Name = "主号" },
                    new PuloniaGenshinAccountProfile { Id = accountB, Name = "小号", Uid = "100000002" }];
                await store.SaveAccountSettingsAsync(settingsFile);
                await store.IncrementBossRewardCountAsync(accountA, "plan", "task", "");
                await store.IncrementBossRewardCountAsync(accountA, "plan", "task", "");
                await store.IncrementBossRewardCountAsync(accountB, "plan", "task", "");
            }
            using var reopened = new PuloniaTaskStore(directory);
            Assert.Equal(2, await reopened.ReadBossRewardCountAsync(accountA, "plan", "task", ""));
            Assert.Equal(1, await reopened.ReadBossRewardCountAsync(accountB, "plan", "task", ""));
            Assert.Equal(0, await reopened.ReadBossRewardCountAsync(accountA, "plan", "other-task", ""));
            Assert.Equal(0, await reopened.ReadBossRewardCountAsync(accountA, "plan", "task", "new-generation"));
            var file = JObject.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "accounts", accountA, "boss-rewards.json")));
            var entry = (JObject)Assert.Single((JArray)file["rewards"]!);
            Assert.Equal(PuloniaTaskStore.GetBossRewardKey("plan", "task", ""), entry.Value<string>("key"));
            Assert.Equal(("plan", "task", ""), (entry.Value<string>("plan_id"), entry.Value<string>("task_id"), entry.Value<string>("reset_id")));

            // 以界面显示路线 CD 的账户为主：第一行写明该账户（0 次也显示），其他有记录的账户另列
            var settings = new AutoBossSettingsViewModel(new JObject(), reopened);
            await settings.SetProgressScopeAsync("plan", "task", accountB);
            Assert.Equal($"小号：1 次{Environment.NewLine}其他账户：主号 2 次", settings.CompletedRunCountText);
            await settings.SetProgressScopeAsync("plan", "other-task", accountB);
            Assert.Equal("小号：0 次", settings.CompletedRunCountText);
            // 主账户未知时按账户目录顺序列出有记录的账户
            await settings.SetProgressScopeAsync("plan", "task", null);
            Assert.Equal($"主号：2 次{Environment.NewLine}小号：1 次", settings.CompletedRunCountText);
            await settings.SetProgressScopeAsync("plan", "task", accountA);
            Assert.Equal($"主号：2 次{Environment.NewLine}其他账户：小号 1 次", settings.CompletedRunCountText);
            settings.ResetProgressCommand.Execute(null);
            var changes = new JObject();
            settings.CollectChanges(changes);
            Assert.NotEmpty(changes.Value<string>(PuloniaSoloTaskExecutor.BossRewardCountResetParameter)!);
            Assert.Equal(2, await reopened.ReadBossRewardCountAsync(accountA, "plan", "task", ""));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
