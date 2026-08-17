using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.ViewModel.Pages;
using System.Reflection;

namespace BetterGenshinImpact.UnitTest.GameTaskTests;

public class TaskRunnerCleanupTests
{
    [Fact]
    public void RunAllContinuesAfterCleanupStepFailures()
    {
        var calls = new List<string>();
        var failures = new List<string>();

        TaskRunnerCleanup.RunAll(
        [
            ("release-input", () =>
            {
                calls.Add("release-input");
                throw new InvalidOperationException("SendInput failed");
            }),
            ("clear-triggers", () => calls.Add("clear-triggers")),
            ("close-mask", () =>
            {
                calls.Add("close-mask");
                throw new InvalidOperationException("mask close failed");
            }),
            ("clear-context", () => calls.Add("clear-context"))
        ],
        (name, _) => failures.Add(name));

        Assert.Equal(
            ["release-input", "clear-triggers", "close-mask", "clear-context"],
            calls);
        Assert.Equal(["release-input", "close-mask"], failures);
    }

    [Fact]
    public void RunAllContinuesWhenFailureReporterThrows()
    {
        var finalCleanupRan = false;

        TaskRunnerCleanup.RunAll(
        [
            ("release-input", () => throw new InvalidOperationException("SendInput failed")),
            ("clear-context", () => finalCleanupRan = true)
        ],
        (_, _) => throw new InvalidOperationException("logging failed"));

        Assert.True(finalCleanupRan);
    }

    [Fact]
    public void LockFailureIsPropagatedOnlyForManagedAutomation()
    {
        TaskRunnerFailurePolicy.ThrowIfLockUnavailable(propagateExceptions: false);

        var exception = Assert.Throws<InvalidOperationException>(
            () => TaskRunnerFailurePolicy.ThrowIfLockUnavailable(propagateExceptions: true));

        Assert.Contains("当前存在正在运行中的独立任务", exception.Message);
    }

    [Fact]
    public void ManagedExecutionContractsExposeOptInExceptionPropagation()
    {
        var runThreadAsync = typeof(TaskRunner).GetMethod(nameof(TaskRunner.RunThreadAsync));
        var runnerParameter = Assert.Single(runThreadAsync!.GetParameters().Skip(1));
        Assert.Equal("propagateExceptions", runnerParameter.Name);
        Assert.Equal(false, runnerParameter.DefaultValue);

        var runMulti = typeof(IScriptService).GetMethod(nameof(IScriptService.RunMulti));
        var serviceParameter = runMulti!.GetParameters().Last();
        Assert.Equal("propagateExceptions", serviceParameter.Name);
        Assert.Equal(false, serviceParameter.DefaultValue);

        var runOneDragonAsync = typeof(OneDragonFlowViewModel).GetMethod(
            "RunOneDragonAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var oneDragonParameter = Assert.Single(runOneDragonAsync!.GetParameters());
        Assert.Equal("propagateExceptions", oneDragonParameter.Name);
    }
}
