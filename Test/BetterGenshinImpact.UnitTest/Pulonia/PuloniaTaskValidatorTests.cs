using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 精简后的计划校验：对象环与重复挂载由节点 ID 唯一性拦截，Windows 保留名检查保留，计划复制只校验一次但仍拒绝坏计划。
/// </summary>
public sealed class PuloniaTaskValidatorTests
{
    /// <summary>
    /// 节点挂到自己的子树下形成循环时明确报错，不会无限递归。
    /// </summary>
    [Fact]
    public void ValidatePlan_RejectsCycleByDuplicateId()
    {
        var plan = new PuloniaTaskPlan { Name = "循环" };
        var group = new PuloniaTask { Name = "分组" };
        plan.RootTask.Children.Add(group);
        group.Children.Add(group);

        var error = Assert.Throws<PuloniaTaskValidationException>(() => PuloniaTaskValidator.ValidatePlan(plan));
        Assert.Contains("节点 ID 重复", error.Message);
    }

    /// <summary>
    /// 同一节点对象同时挂在两个父节点下时明确报错。
    /// </summary>
    [Fact]
    public void ValidatePlan_RejectsSharedNode()
    {
        var plan = new PuloniaTaskPlan { Name = "共享" };
        var leaf = new PuloniaTask { Name = "叶子", TaskType = "sample.echo" };
        plan.RootTask.Children.Add(new PuloniaTask { Name = "甲", Children = [leaf] });
        plan.RootTask.Children.Add(new PuloniaTask { Name = "乙", Children = [leaf] });

        Assert.Throws<PuloniaTaskValidationException>(() => PuloniaTaskValidator.ValidatePlan(plan));
        Assert.Throws<PuloniaTaskValidationException>(() => PuloniaTaskJson.ClonePlan(plan));
    }

    /// <summary>
    /// 命令行传入的计划 ID 会拼成文件路径，Windows 保留名仍被拒绝。
    /// </summary>
    [Theory]
    [InlineData("con")]
    [InlineData("nul")]
    [InlineData("com1")]
    public void ValidateId_RejectsWindowsReservedNames(string id)
        => Assert.Throws<PuloniaTaskValidationException>(() => PuloniaTaskValidator.ValidateId(id, "cli"));

    /// <summary>
    /// 计划复制保留全部内容和稳定 ID，副本与原计划输出逐字一致且相互独立。
    /// </summary>
    [Fact]
    public void ClonePlan_KeepsContentAndIsIndependent()
    {
        var plan = new PuloniaTaskPlan { Name = "复制" };
        plan.RootTask.Children.Add(new PuloniaTask
        {
            Name = "叶子", TaskType = "sample.echo",
            Parameters = new JObject { ["value"] = 1.5, ["none"] = null, ["flag"] = false },
            Policy = new PuloniaTaskPolicy { MaxRetries = 2, RetryDelaySeconds = 0 }
        });

        var clone = PuloniaTaskJson.ClonePlan(plan);
        clone.RootTask.Children[0].Parameters["value"] = 2;

        Assert.Equal(1.5, plan.RootTask.Children[0].Parameters.Value<double>("value"));
        clone.RootTask.Children[0].Parameters["value"] = 1.5;
        Assert.Equal(PuloniaTaskJson.WritePlan(plan), PuloniaTaskJson.WritePlan(clone));
    }
}
