using BetterGenshinImpact.GameTask.AutoFight.Script;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFightTests;

public class CombatScriptBagTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SelectCombatScript_FullMatch_PrefersDedicatedScriptRegardlessOfOrder(bool universalFirst)
    {
        var universal = CreateScript("通用", "A", "B", "C", "D", "E", "F");
        var dedicated = CreateScript("专用", "A", "B", "C", "D");
        List<CombatScript> scripts = universalFirst ? [universal, dedicated] : [dedicated, universal];
        var bag = new CombatScriptBag(scripts);

        var (selected, matchCount) = bag.SelectCombatScript(["A", "B", "C", "D"]);

        Assert.Same(dedicated, selected);
        Assert.Equal(4, matchCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SelectCombatScript_PartialMatch_PrefersHigherRatioRegardlessOfOrder(bool broaderFirst)
    {
        var broader = CreateScript("更多角色", "A", "B", "C", "E", "F");
        // 两个策略的比例都小于 100%，也应优先选择 3/4 而非 3/5。
        var focused = CreateScript("较少角色", "A", "B", "C", "E");
        List<CombatScript> scripts = broaderFirst ? [broader, focused] : [focused, broader];
        var bag = new CombatScriptBag(scripts);

        var (selected, matchCount) = bag.SelectCombatScript(["A", "B", "C", "D"]);

        Assert.Same(focused, selected);
        Assert.Equal(3, matchCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SelectCombatScript_PrefersMatchCountOverRatio(bool fullMatch)
    {
        // 比例为 100% 的小策略，不能超过匹配人数更多的策略。
        var focused = CreateScript("两人策略", "A", "B");
        var broader = fullMatch
            ? CreateScript("全队匹配", "A", "B", "C", "D", "E", "F")
            : CreateScript("部分匹配", "A", "B", "C", "E", "F");
        var bag = new CombatScriptBag([focused, broader]);

        var (selected, matchCount) = bag.SelectCombatScript(["A", "B", "C", "D"]);

        Assert.Same(broader, selected);
        Assert.Equal(fullMatch ? 4 : 3, matchCount);
    }

    [Fact]
    public void SelectCombatScript_EqualScores_PreservesOrderAcrossTeamChanges()
    {
        var first = CreateScript("第一个", "A", "B");
        var second = CreateScript("第二个", "A", "C");
        var scripts = new List<CombatScript> { first, second };
        var bag = new CombatScriptBag(scripts);

        // 先让第二个策略胜出，再换成两者同分的队伍。
        // 之前的选择不能改变后续同分时的优先顺序。
        Assert.Same(second, bag.SelectCombatScript(["C", "D"]).Script);
        Assert.Same(first, bag.SelectCombatScript(["A", "D"]).Script);
        Assert.Same(first, scripts[0]);
        Assert.Same(second, scripts[1]);
    }

    [Fact]
    public void SelectCombatScript_NoDedicatedMatch_FallsBackToUniversalScript()
    {
        var unrelated = CreateScript("其他队伍", "X", "Y");
        var universal = CreateScript("通用", "A", "B", "C", "D", "E", "F");
        var bag = new CombatScriptBag([unrelated, universal]);

        var (selected, matchCount) = bag.SelectCombatScript(["A", "B", "C", "D"]);

        Assert.Same(universal, selected);
        Assert.Equal(4, matchCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SelectCombatScript_SmallerTeam_PrefersDedicatedScript(int teamSize)
    {
        string[] allNames = ["A", "B", "C", "D"];
        var team = allNames.Take(teamSize).ToArray();
        var universal = CreateScript("通用", allNames);
        var dedicated = CreateScript("当前队伍", team);
        var bag = new CombatScriptBag([universal, dedicated]);

        var (selected, matchCount) = bag.SelectCombatScript(team);

        Assert.Same(dedicated, selected);
        Assert.Equal(teamSize, matchCount);
    }

    [Fact]
    public void SelectCombatScript_EmptyScript_IsSkipped()
    {
        var empty = CreateScript("空脚本");
        var matching = CreateScript("可用策略", "A");
        var bag = new CombatScriptBag([empty, matching]);

        Assert.Same(matching, bag.SelectCombatScript(["A"]).Script);
    }

    [Fact]
    public void SelectCombatScript_EmptyBag_ThrowsNoMatch()
    {
        var bag = new CombatScriptBag(new List<CombatScript>());

        var exception = Assert.Throws<Exception>(() => bag.SelectCombatScript(["A"]));

        Assert.Equal("未匹配到任何战斗脚本", exception.Message);
    }

    [Fact]
    public void SelectCombatScript_NoMatchingCharacter_ThrowsNoMatch()
    {
        var bag = new CombatScriptBag(CreateScript("其他队伍", "X", "Y"));

        var exception = Assert.Throws<Exception>(() => bag.SelectCombatScript(["A", "B"]));

        Assert.Equal("未匹配到任何战斗脚本", exception.Message);
    }

    [Fact]
    public void SelectCombatScript_EmptyTeam_ThrowsNoMatch()
    {
        var bag = new CombatScriptBag(CreateScript("可用策略", "A"));

        var exception = Assert.Throws<Exception>(() => bag.SelectCombatScript([]));

        Assert.Equal("未匹配到任何战斗脚本", exception.Message);
    }

    private static CombatScript CreateScript(string name, params string[] avatarNames)
    {
        // 直接构造策略，避免依赖角色配置、图像识别或 WPF 初始化。
        return new CombatScript(new HashSet<string>(avatarNames), []) { Name = name };
    }
}
