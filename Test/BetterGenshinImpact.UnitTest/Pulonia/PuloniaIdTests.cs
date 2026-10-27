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
        Assert.Matches("^prf_[0123456789abcdefghjkmnpqrstvwxyz]{16}$", PuloniaId.NewProfileId());
        Assert.Matches("^trg_[0123456789abcdefghjkmnpqrstvwxyz]{16}$", PuloniaId.NewTriggerId());
        Assert.Matches("^acc_[0123456789abcdefghjkmnpqrstvwxyz]{16}$", PuloniaId.NewAccountId());
        Assert.StartsWith("acc_", new PuloniaGenshinAccountProfile().Id, StringComparison.Ordinal);
    }

    /// <summary>
    /// 运行 ID 带本地时间便于在历史目录中辨认，展开节点 ID 由相对路径确定性派生且忽略大小写。
    /// </summary>
    [Fact]
    public void RunAndResourceIds_AreReadableAndDeterministic()
    {
        var time = new DateTimeOffset(2026, 10, 24, 15, 30, 12, TimeSpan.Zero);
        var runId = PuloniaId.NewRunId(time);
        Assert.Matches("^run_\\d{8}-\\d{6}-[0123456789abcdefghjkmnpqrstvwxyz]{4}$", runId);
        Assert.StartsWith("run_" + time.ToLocalTime().ToString("yyyyMMdd-HHmmss") + "-", runId, StringComparison.Ordinal);
        PuloniaTaskValidator.ValidateId(runId, "run");

        var resourceId = PuloniaId.FromResourcePath("蒙德/风车菊-01.json");
        Assert.Matches("^res_[0123456789abcdefghjkmnpqrstvwxyz]{16}$", resourceId);
        Assert.Equal(resourceId, PuloniaId.FromResourcePath("蒙德/风车菊-01.JSON"));
        Assert.NotEqual(resourceId, PuloniaId.FromResourcePath("蒙德/风车菊-02.json"));
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
    /// 新建模型和复制操作都使用对应实体类型的短 ID；ID 上限为 32 位，避免成为路径中的长段。
    /// </summary>
    [Fact]
    public void ModelsAndCopies_UseTypedIdsAndLimitLength()
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
        Assert.StartsWith("prf_", new PuloniaSettingsProfile().Id, StringComparison.Ordinal);
        Assert.StartsWith("trg_", plan.Triggers[0].Id, StringComparison.Ordinal);
        Assert.StartsWith("pln_", copy.Id, StringComparison.Ordinal);
        Assert.All(EnumerateTasks(copy.RootTask), task => Assert.StartsWith("tsk_", task.Id, StringComparison.Ordinal));
        Assert.All(copy.Triggers, trigger => Assert.StartsWith("trg_", trigger.Id, StringComparison.Ordinal));

        PuloniaTaskValidator.ValidateId(new string('a', 32), "max");
        Assert.Throws<PuloniaTaskValidationException>(() => PuloniaTaskValidator.ValidateId(new string('a', 33), "too-long"));
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
