using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 整份计划复制、文件删除和保存并发回归，不启动应用或游戏。
/// </summary>
public sealed class PuloniaTaskPlanManagementTests : IDisposable
{
    /// <summary>
    /// 测试独占目录，避免接触程序真实计划和历史。
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>
    /// 整份复制重新映射触发目标，保留参数类型、共享设置、方案、引用和配置，且不修改来源。
    /// </summary>
    [Fact]
    public void CopyPlan_RemapsInternalReferencesAndPreservesIndependentConfiguration()
    {
        var plan = CreatePlan("来源计划");
        var leaf = plan.RootTask.Children[0];
        leaf.IsEnabled = false;
        leaf.ProfileId = "shared-profile";
        leaf.Settings["shell.timeout_seconds"] = 5;
        leaf.Policy.MaxRetries = 2;
        plan.Description = "计划说明";
        plan.RootTask.Children.Add(new PuloniaTask
        {
            Name = "引用计划", Source = new PuloniaTaskSource { Kind = "plan", PlanId = "other-plan" }
        });
        plan.Accounts.Add(new PuloniaTaskAccountBinding
        {
            AccountId = "account"
        });
        plan.Triggers.Add(new PuloniaTaskTrigger
        {
            Enabled = true, TargetTaskId = leaf.Id, AccountId = "account", Kind = PuloniaTaskTriggerKind.Hotkey
        });
        plan.Triggers.Add(new PuloniaTaskTrigger { Enabled = true });
        plan.Revision = 7;
        var originalJson = PuloniaTaskJson.WritePlan(plan);

        var copy = PuloniaTaskJson.CopyPlan(plan);
        var copiedLeaf = copy.RootTask.Children[0];
        Assert.NotEqual(plan.Id, copy.Id);
        Assert.Equal(0, copy.Revision);
        Assert.NotEqual(plan.RootTask.Id, copy.RootTask.Id);
        for (var i = 0; i < plan.RootTask.Children.Count; i++)
            Assert.NotEqual(plan.RootTask.Children[i].Id, copy.RootTask.Children[i].Id);
        Assert.Equal(plan.Description, copy.Description);
        Assert.False(copiedLeaf.IsEnabled);
        Assert.Equal(leaf.ProfileId, copiedLeaf.ProfileId);
        Assert.True(JToken.DeepEquals(leaf.Settings, copiedLeaf.Settings));
        Assert.Equal(2, copiedLeaf.Policy.MaxRetries);
        Assert.True(JToken.DeepEquals(leaf.Parameters, copiedLeaf.Parameters));
        Assert.Equal("other-plan", copy.RootTask.Children[1].Source!.PlanId);
        Assert.Equal("account", Assert.Single(copy.Accounts).AccountId);
        Assert.Equal(copiedLeaf.Id, copy.Triggers[0].TargetTaskId);
        Assert.Null(copy.Triggers[1].TargetTaskId);
        Assert.All(copy.Triggers, trigger => Assert.False(trigger.Enabled));
        Assert.NotEqual(plan.Triggers[0].Id, copy.Triggers[0].Id);
        Assert.NotEqual(plan.Triggers[1].Id, copy.Triggers[1].Id);

        copiedLeaf.Parameters["values"] = new JArray(99);
        copiedLeaf.Policy.MaxRetries = 10;
        copy.RootTask.Children[1].Source!.PlanId = "changed-plan";
        Assert.Equal(originalJson, PuloniaTaskJson.WritePlan(plan));
    }

    /// <summary>
    /// 删除清理正式文件与备份，并通知调度刷新；已有历史和运行状态原文保持不变。
    /// </summary>
    [Fact]
    public async Task DeletePlan_RemovesFilesAndPreservesHistoryAndState()
    {
        using var store = new PuloniaTaskStore(_directory);
        var plan = await store.SavePlanAsync(CreatePlan("待删除"));
        plan = await store.SavePlanAsync(plan);
        var record = new PuloniaTaskRunRecord
        {
            RunId = PuloniaId.NewRunId(DateTimeOffset.Now), Request = new PuloniaTaskRequest { PlanId = plan.Id },
            PlanName = plan.Name, SnapshotJson = "{}", Status = PuloniaTaskRunStatus.Succeeded,
            Message = "完成", SubmittedAt = DateTimeOffset.UtcNow
        };
        await store.ArchiveRunAsync(record);
        await store.SaveStateAsync(new PuloniaTaskState());
        var statePath = Path.Combine(_directory, "state.json");
        var stateJson = await File.ReadAllTextAsync(statePath);
        var path = Path.Combine(_directory, "plans", plan.Id + ".json");
        Assert.True(File.Exists(path + ".bak"));
        var version = store.PlanChangeVersion;

        await store.DeletePlanAsync(plan.Id, plan.Revision);

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".bak"));
        Assert.Null(await store.LoadPlanAsync(plan.Id));
        Assert.Empty(await store.ListPlansAsync());
        Assert.Equal(version + 1, store.PlanChangeVersion);
        Assert.Equal(record.RunId, Assert.Single(await store.ListHistoryAsync()).RunId);
        Assert.Equal(stateJson, await File.ReadAllTextAsync(statePath));
    }

    /// <summary>
    /// 即使引用节点禁用，删除也必须拒绝；先移除并保存引用后才允许删除。
    /// </summary>
    [Fact]
    public async Task DeletePlan_RejectsReferencesUntilTheyAreRemoved()
    {
        using var store = new PuloniaTaskStore(_directory);
        var target = await store.SavePlanAsync(CreatePlan("引用目标"));
        var source = CreatePlan("引用来源");
        source.RootTask.Children.Add(new PuloniaTask
        {
            Name = "禁用引用", IsEnabled = false, Source = new PuloniaTaskSource { Kind = "plan", PlanId = target.Id }
        });
        source = await store.SavePlanAsync(source);
        var version = store.PlanChangeVersion;

        var error = await Assert.ThrowsAsync<PuloniaTaskValidationException>(
            () => store.DeletePlanAsync(target.Id, target.Revision));
        Assert.Contains(source.Name, error.Message);
        Assert.NotNull(await store.LoadPlanAsync(target.Id));
        Assert.Equal(version, store.PlanChangeVersion);

        source.RootTask.Children.RemoveAt(1);
        await store.SavePlanAsync(source);
        await store.DeletePlanAsync(target.Id, target.Revision);
        Assert.Equal(source.Id, Assert.Single(await store.ListPlansAsync()).Id);
    }

    /// <summary>
    /// 其他存储实例已保存新修订时，不允许旧编辑会话删除更新后的计划。
    /// </summary>
    [Fact]
    public async Task DeletePlan_RejectsStaleRevision()
    {
        using var store = new PuloniaTaskStore(_directory);
        using var otherStore = new PuloniaTaskStore(_directory);
        var plan = await store.SavePlanAsync(CreatePlan("旧修订"));
        var oldRevision = plan.Revision;
        plan.Name = "其他实例的新修订";
        plan = await otherStore.SavePlanAsync(plan);

        await Assert.ThrowsAsync<PuloniaTaskValidationException>(() => store.DeletePlanAsync(plan.Id, oldRevision));
        Assert.Equal(plan.Name, (await store.LoadPlanAsync(plan.Id))!.Name);
        Assert.True(File.Exists(Path.Combine(_directory, "plans", plan.Id + ".json.bak")));
    }

    /// <summary>
    /// 建立包含数值、布尔和空值参数的简单计划。
    /// </summary>
    private static PuloniaTaskPlan CreatePlan(string name) => new()
    {
        Name = name,
        RootTask = new PuloniaTask
        {
            Name = name, Children =
            [new PuloniaTask
            {
                Name = "计算", TaskType = "csharp",
                Parameters = new JObject { ["operation"] = "sample.sum", ["values"] = new JArray(1, 2), ["flag"] = false, ["empty"] = null }
            }]
        }
    };

    /// <summary>
    /// 仅清理当前测试创建的唯一临时目录。
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
