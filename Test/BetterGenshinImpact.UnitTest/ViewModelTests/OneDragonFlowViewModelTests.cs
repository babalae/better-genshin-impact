using BetterGenshinImpact.ViewModel.Pages;
using System.Reflection;

namespace BetterGenshinImpact.UnitTest.ViewModelTests;

public class OneDragonFlowViewModelTests
{
    [Fact]
    public void CompletionActionListContainsImmediateAndDelayedShutdownOptions()
    {
        var optionsProperty = typeof(OneDragonFlowViewModel).GetProperty(
            "CompletionActionOptions",
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(optionsProperty);
        var options = Assert.IsAssignableFrom<IReadOnlyList<string>>(optionsProperty!.GetValue(null));
        Assert.Contains("关机", options);
        Assert.Contains("关机（1分钟后）", options);
    }
}
