using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.UnitTest.Helpers;

public class CommandLineOptionsTests
{
    [Fact]
    public void Parse_ShouldRecognizeBetterGiStartUrl()
    {
        var options = CommandLineOptions.Parse(["BetterGI.exe", "bettergi://start"]);

        Assert.Equal(CommandLineAction.Start, options.Action);
        Assert.True(options.HasTaskArgs);
    }

    [Fact]
    public void Parse_ShouldKeepStartGroupNamesInOrder()
    {
        var options = CommandLineOptions.Parse(["BetterGI.exe", "--startGroups", "A", "B"]);

        Assert.Equal(CommandLineAction.StartGroups, options.Action);
        Assert.Equal(["A", "B"], options.GroupNames);
    }

    [Fact]
    public void Parse_ShouldRecognizeTaskProgressName()
    {
        var options = CommandLineOptions.Parse(["BetterGI.exe", "--TaskProgress", "latest"]);

        Assert.Equal(CommandLineAction.TaskProgress, options.Action);
        Assert.Equal(["latest"], options.GroupNames);
    }

    [Fact]
    public void Parse_ShouldKeepOneDragonConfigName()
    {
        var options = CommandLineOptions.Parse(["BetterGI.exe", "startOneDragon", "daily"]);

        Assert.Equal(CommandLineAction.StartOneDragon, options.Action);
        Assert.Equal("daily", options.OneDragonConfigName);
    }

    [Fact]
    public void ParseActivationArgs_ShouldParseArgumentsWithoutExecutableName()
    {
        var options = CommandLineOptions.ParseActivationArgs(["bettergi://start"]);

        Assert.Equal(CommandLineAction.Start, options.Action);
    }
}
