using BetterGenshinImpact.GameTask.AutoFight.Script;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFightTests;

public class CombatScriptParserTests
{
    /// <summary>
    /// 无角色前缀的指令中含有空格参数（如 walk(s, 0.2)），不应将空格误识别为角色分隔符
    /// </summary>
    [Fact]
    public void ParseLine_NoPrefixWithSpacedArg_PreservesWholeLineAsCommands()
    {
        // 无 validate 模式，执行 ParseContext
        var script = CombatScriptParser.ParseContext("walk(s, 0.2)", validate: false);
        Assert.Single(script.CombatCommands);
        Assert.Contains("walk", script.CombatCommands[0].Method.Alias);
        Assert.NotNull(script.CombatCommands[0].Args);
        Assert.Equal(2, script.CombatCommands[0].Args.Count);
        Assert.Equal("s", script.CombatCommands[0].Args[0]);
        Assert.Equal("0.2", script.CombatCommands[0].Args[1]);
    }

    /// <summary>
    /// 有角色前缀的指令正常解析
    /// </summary>
    [Fact]
    public void ParseLine_WithCharPrefix_SplitsCorrectly()
    {
        var script = CombatScriptParser.ParseContext("娜维娅 e", validate: false);
        Assert.Single(script.CombatCommands);
        Assert.Contains("e", script.CombatCommands[0].Method.Alias);
    }

    /// <summary>
    /// 有角色前缀且指令含有空格参数（如 娜维娅 walk(s, 0.2)），应正确拆分
    /// </summary>
    [Fact]
    public void ParseLine_CharPrefixWithSpacedArg_SplitsCorrectly()
    {
        var script = CombatScriptParser.ParseContext("娜维娅 walk(s, 0.2)", validate: false);
        Assert.Single(script.CombatCommands);
        Assert.Contains("walk", script.CombatCommands[0].Method.Alias);
        Assert.NotNull(script.CombatCommands[0].Args);
        Assert.Equal(2, script.CombatCommands[0].Args.Count);
        Assert.Equal("s", script.CombatCommands[0].Args[0]);
        Assert.Equal("0.2", script.CombatCommands[0].Args[1]);
    }

    /// <summary>
    /// 无角色前缀时传入 defaultAvatarName，应使用该名称作为角色名并正常解析指令
    /// </summary>
    [Fact]
    public void ParseLine_NoPrefixWithDefaultAvatarName_UsesProvidedName()
    {
        var script = CombatScriptParser.ParseContext("walk(s, 0.2)", validate: true, defaultAvatarName: "娜维娅");
        Assert.Single(script.CombatCommands);
        Assert.Equal("娜维娅", script.CombatCommands[0].Name);
        Assert.Contains("walk", script.CombatCommands[0].Method.Alias);
        Assert.NotNull(script.CombatCommands[0].Args);
        Assert.Equal(2, script.CombatCommands[0].Args.Count);
        Assert.Equal("s", script.CombatCommands[0].Args[0]);
        Assert.Equal("0.2", script.CombatCommands[0].Args[1]);
    }
}
