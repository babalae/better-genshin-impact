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
                new WalkSymbol(WalkDirection.Backward, [new DoubleSymbol(0.1)], [], new CommaTriviaSymbol()),
                new SkillSymbol(true, [new HoldSymbol()], [new SpaceTriviaSymbol()], new CommaTriviaSymbol()),
                new WaitSymbol([new DoubleSymbol(0.3)], [new SpaceTriviaSymbol()], new CommaTriviaSymbol()),
                new WalkSymbol(WalkDirection.Forward, [new DoubleSymbol(0.1)], [new SpaceTriviaSymbol()], default),
            ])),
            new LineBreakTriviaSymbol(),
            new AvatarInstructionListSymbol(new("芙宁娜"), [new SpaceTriviaSymbol()], new(
            [
                new SkillSymbol(true, [], new CommaTriviaSymbol()),
                new BurstSymbol(true, [new SpaceTriviaSymbol()], default)
            ])),
            new LineBreakTriviaSymbol(),
            new AvatarInstructionListSymbol(new("行秋"), [new SpaceTriviaSymbol()], new(
            [
                new SkillSymbol(true, [], new CommaTriviaSymbol()),
                new BurstSymbol(true, [new SpaceTriviaSymbol()], new CommaTriviaSymbol()),
                new SkillSymbol(true, [new SpaceTriviaSymbol()], default),
            ])),
        ]);

        Console.WriteLine(scriptUnit.Emit(new SymbolEmitter()));
    }

    [TestMethod]
    public void TestParse()
    {
        ReadOnlySpan<char> raw = """
            // 测试注释
            钟离 s(0.1),e(hold),wait(0.3),w(0.1)
            芙宁娜 e,q
            
            行秋 e,q,e
            """;
        ScriptUnit scriptUnit = SymbolParser.Parse(raw);
        
        Console.WriteLine(scriptUnit.Emit(new SymbolEmitter()));
    }
}