using System.Globalization;
using BetterGenshinImpact.GameTask.AutoFight.Script;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFightTests;

public class CombatCommandCultureTests
{
    /// <summary>
    /// 小数点为 ',' 的区域（pt-BR、de-DE 等）下，"0.85" 也必须按 0.85 秒解析，
    /// 而不是把 '.' 当千分位读成 85 秒。
    /// </summary>
    [Theory]
    [InlineData("pt-BR")]
    [InlineData("de-DE")]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void ParseSeconds_IgnoresCurrentCulture(string cultureName)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            Assert.Equal(0.85, CombatCommand.ParseSeconds("0.85"));
            Assert.Equal(0.5, CombatCommand.ParseSeconds("0.5"));
            Assert.Equal(0.2, CombatCommand.ParseSeconds("0.2"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// 构造函数是不依赖游戏即可触达的调用点：walk(s, 0.2) 在 fr-FR / ru-RU 下，
    /// 旧的 double.Parse(Args[1]) 会因 '.' 不是小数点也不是千分位而抛 FormatException。
    /// </summary>
    [Theory]
    [InlineData("fr-FR")]
    [InlineData("ru-RU")]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    public void WalkConstructor_ParsesDecimalSecondsInAnyCulture(string cultureName)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var command = new CombatCommand("芙宁娜", "walk(s, 0.2)");
            Assert.Equal("0.2", command.Args![1]);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// 端到端：解析 issue 里的整行策略，参数文本保持原样，换算出的时长在 pt-BR 下仍是 850 毫秒。
    /// </summary>
    [Fact]
    public void AttackArg_FromScriptLine_IsMillisecondsInPtBr()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            var script = CombatScriptParser.ParseContext("芙宁娜 e, attack(0.85), check, attack(0.2)", validate: false);
            var attack = script.CombatCommands[1];
            Assert.Equal("0.85", attack.Args![0]);
            Assert.Equal(850, (int)System.TimeSpan.FromSeconds(CombatCommand.ParseSeconds(attack.Args[0])).TotalMilliseconds);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
