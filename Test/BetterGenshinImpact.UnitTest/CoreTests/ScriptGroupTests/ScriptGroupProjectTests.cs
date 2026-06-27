using BetterGenshinImpact.Core.Script.Group;

namespace BetterGenshinImpact.UnitTest.CoreTests.ScriptGroupTests;

public class ScriptGroupProjectTests
{
    [Fact]
    public void ResolveJsScriptProjectName_ShouldUseTrimmedCustomName()
    {
        var name = ScriptGroupProject.ResolveJsScriptProjectName("默认脚本名", "  自定义采集名  ");

        Assert.Equal("自定义采集名", name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveJsScriptProjectName_ShouldFallbackToDefaultNameWhenCustomNameIsBlank(string? customName)
    {
        var name = ScriptGroupProject.ResolveJsScriptProjectName("默认脚本名", customName);

        Assert.Equal("默认脚本名", name);
    }

    [Fact]
    public void RenameDisplayName_ShouldTrimAndUpdateName()
    {
        var project = new ScriptGroupProject("默认脚本名", "ScriptFolder", "Javascript");

        var renamed = project.RenameDisplayName("  新脚本名  ");

        Assert.True(renamed);
        Assert.Equal("新脚本名", project.Name);
    }

    [Fact]
    public void RenameDisplayName_ShouldNotifyNameChange()
    {
        var project = new ScriptGroupProject("默认脚本名", "ScriptFolder", "Javascript");
        var notified = false;
        project.PropertyChanged += (_, args) => notified = args.PropertyName == nameof(ScriptGroupProject.Name);

        project.RenameDisplayName("新脚本名");

        Assert.True(notified);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RenameDisplayName_ShouldKeepNameWhenNewNameIsBlank(string? newName)
    {
        var project = new ScriptGroupProject("默认脚本名", "ScriptFolder", "Javascript");

        var renamed = project.RenameDisplayName(newName);

        Assert.False(renamed);
        Assert.Equal("默认脚本名", project.Name);
    }
}
