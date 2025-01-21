namespace BetterGenshinImpact.CombatScript.Test;

[TestClass]
public sealed class ScriptEmitTest
{
    [TestMethod]
    public void TestEmit()
    {
        ScriptUnit scriptUnit = new(
        [
            new CommentSymbol("测试注释"),
            new LineBreakTriviaSymbol(),
            new AvatarInstructionListSymbol(new("钟离"), [new SpaceTriviaSymbol()], new(
            [
                new WalkSymbol(WalkDirection.Backward, [new DoubleSymbol(0.1)], [new CommaTriviaSymbol()]),
                new SkillSymbol(true, [new HoldSymbol()], [new CommaTriviaSymbol()]),
                new WaitSymbol([new DoubleSymbol(0.3)], [new CommaTriviaSymbol()]),
                new WalkSymbol(WalkDirection.Forward, [new DoubleSymbol(0.1)], []),
            ])),
            new LineBreakTriviaSymbol(),
            new AvatarInstructionListSymbol(new("芙宁娜"), [new SpaceTriviaSymbol()], new(
            [
                new SkillSymbol(true, [new CommaTriviaSymbol()]),
                new BurstSymbol(true, [])
            ])),
            new LineBreakTriviaSymbol(),
            new AvatarInstructionListSymbol(new("行秋"), [new SpaceTriviaSymbol()], new(
            [
                new SkillSymbol(true, [new CommaTriviaSymbol()]),
                new BurstSymbol(true, [new CommaTriviaSymbol()]),
                new SkillSymbol(true, []),
            ])),
        ]);

        Console.WriteLine(scriptUnit.Emit(new DefaultSymbolEmitter()));
    }

    [TestMethod]
    public void Test()
    {
        ReadOnlySpan<char> raw = "ABCDEF;GHIJKL\r\nMNOPQR\nSTUVWX\rYZ\r\n";
        SymbolParser parser = new();
        ScriptUnit scriptUnit = parser.Parse(raw);
    }
}