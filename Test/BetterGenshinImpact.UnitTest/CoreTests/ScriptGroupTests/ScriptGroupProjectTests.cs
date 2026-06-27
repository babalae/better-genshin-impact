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
}
