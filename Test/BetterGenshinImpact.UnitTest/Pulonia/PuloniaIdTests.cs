using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// Pulonia 持久化实体短 ID 的格式、唯一性和兼容性回归测试。
/// </summary>
public sealed class PuloniaIdTests
{
    /// <summary>
    /// 各实体使用明确的类型前缀和固定长度小写 Base32 随机部分。
    /// </summary>
    [Fact]
    public void NewIds_UseTypedBase32Format()
    {
        Assert.Matches("^pln_[0123456789abcdefghjkmnpqrstvwxyz]{16}$", PuloniaId.NewPlanId());
        Assert.Matches("^tsk_[0123456789abcdefghjkmnpqrstvwxyz]{16}$", PuloniaId.NewTaskId());
        Assert.Matches("^pre_[0123456789abcdefghjkmnpqrstvwxyz]{16}$", PuloniaId.NewPresetId());
        Assert.Matches("^trg_[0123456789abcdefghjkmnpqrstvwxyz]{16}$", PuloniaId.NewTriggerId());
    }

    /// <summary>
    /// 连续生成的短 ID 不重复，并且能够通过现有通用 ID 校验。
    /// </summary>
    [Fact]
    public void NewIds_AreUniqueAndPassValidation()
    {
        var ids = Enumerable.Range(0, 10000).Select(_ => PuloniaId.NewTaskId()).ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => PuloniaTaskValidator.ValidateId(id, "test"));
    }

    /// <summary>
    /// 新建模型和复制操作都使用对应实体类型的短 ID，并保留旧 ID 的读取能力。
    /// </summary>
    [Fact]
    public void ModelsAndCopies_UseTypedIdsAndKeepLegacyIdsValid()
    {
        var plan = new PuloniaTaskPlan
        {
            RootTask = new PuloniaTask
            {
                Children = [new PuloniaTask { Name = "任务", TaskType = "csharp" }]
            },
            Triggers = [new PuloniaTaskTrigger()]
        };

        var copy = PuloniaTaskJson.CopyPlan(plan);

        Assert.StartsWith("pln_", plan.Id, StringComparison.Ordinal);
        Assert.StartsWith("tsk_", plan.RootTask.Id, StringComparison.Ordinal);
        Assert.StartsWith("pre_", new PuloniaTaskPreset().Id, StringComparison.Ordinal);
        Assert.StartsWith("trg_", plan.Triggers[0].Id, StringComparison.Ordinal);
        Assert.StartsWith("pln_", copy.Id, StringComparison.Ordinal);
        Assert.All(EnumerateTasks(copy.RootTask), task => Assert.StartsWith("tsk_", task.Id, StringComparison.Ordinal));
        Assert.All(copy.Triggers, trigger => Assert.StartsWith("trg_", trigger.Id, StringComparison.Ordinal));

        PuloniaTaskValidator.ValidateId(Guid.NewGuid().ToString("N"), "legacy");
    }

    /// <summary>
    /// 递归枚举任务树，覆盖复制后的全部节点身份。
    /// </summary>
    private static IEnumerable<PuloniaTask> EnumerateTasks(PuloniaTask task)
    {
        yield return task;
        foreach (var child in task.Children)
            foreach (var descendant in EnumerateTasks(child))
                yield return descendant;
    }
}
