using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Strategy;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFightTests;

/// <summary>验证所有引用写法、规范值、越界处理、内容识别与路由。</summary>
[Collection("CombatStrategyFiles")]
public sealed class CombatStrategyResolverTests : IDisposable
{
    /// <summary>本测试独占的策略文件。</summary>
    private readonly StrategyTestFiles _files = new();

    /// <summary>同名优先 JSON，显式扩展名仍能唯一选择文本。</summary>
    [Fact]
    public void Parse_SameName_PreservesExplicitTextAndJsonPriority()
    {
        var txt = _files.Fight("深渊\\队伍.txt");
        var json = _files.Fight("深渊\\队伍.json", "{}");
        var name = Path.Combine(_files.Prefix, "深渊", "队伍");
        AssertReference(name, CombatStrategyKind.Json, json, name);
        AssertReference(name + ".json", CombatStrategyKind.Json, json, name);
        AssertReference(name + ".txt", CombatStrategyKind.Txt, txt, name + ".txt");
        AssertReference(txt, CombatStrategyKind.Txt, txt, name + ".txt");
    }

    /// <summary>支持单独的文本、JSON、固定项、范围和录制引用。</summary>
    [Fact]
    public void Parse_AllReferenceForms_AreCanonicalAndIdempotent()
    {
        var txt = _files.Fight("仅文本.txt");
        var json = _files.Fight("仅JSON.json", "{}");
        var macro = _files.Macro("AutoFight\\录制.json");
        AssertReference(Path.Combine(_files.Prefix, "仅文本"), CombatStrategyKind.Txt, txt);
        AssertReference(Path.Combine(_files.Prefix, "仅JSON"), CombatStrategyKind.Json, json);
        AssertReference(null, CombatStrategyKind.AutoSelect, Path.GetFullPath(Global.Absolute(@"User\AutoFight")), CombatStrategyResolver.AutoSelectName);
        AssertReference("根据队伍自动选择", CombatStrategyKind.AutoSelect, Path.GetFullPath(Global.Absolute(@"User\AutoFight")));
        AssertReference(AutoFightParam.ComboStrategyName, CombatStrategyKind.Combo, AutoFightParam.ComboStrategyName);
        AssertReference("auto:" + _files.Prefix, CombatStrategyKind.AutoSelect, Path.GetDirectoryName(txt)!);
        var key = "keymouse:" + Path.Combine(_files.Prefix, "AutoFight", "录制");
        AssertReference(key, CombatStrategyKind.KeyMouse, macro, key);
        AssertReference(key + ".json", CombatStrategyKind.KeyMouse, macro, key);
        AssertReference(macro, CombatStrategyKind.KeyMouse, macro, key);
        AssertReference(Path.GetDirectoryName(txt)!, CombatStrategyKind.AutoSelect, Path.GetDirectoryName(txt)!);
    }

    /// <summary>外部 JSON 仅识别顶层 macroEvents，所属根目录的类型优先于内容。</summary>
    [Fact]
    public void Parse_AbsoluteJson_UsesRootAndTopLevelContent()
    {
        var macro = _files.External("macro.json", "{\"info\":{},\"macroEvents\":[]}");
        var nested = _files.External("nested.json", "{\"info\":{\"macroEvents\":[]}}");
        AssertReference(macro, CombatStrategyKind.KeyMouse, macro);
        AssertReference(nested, CombatStrategyKind.Json, nested);
        Assert.Equal(CombatStrategyKind.Json, CombatStrategyResolver.Parse(_files.Fight("伪装.json", "{\"macroEvents\":[]}")).Kind);
        Assert.Equal(CombatStrategyKind.KeyMouse, CombatStrategyResolver.Parse(_files.Macro("伪装.json", "{}")).Kind);
    }

    /// <summary>越界引用即使指向存在文件也必须拒绝，重复解析不能把它转为外部绝对路径。</summary>
    [Theory]
    [InlineData("auto:..\\AutoFight-other")]
    [InlineData("keymouse:..\\..\\outside.json")]
    [InlineData("..\\KeyMouseScript\\outside.json")]
    [InlineData("无效\0路径")]
    public void Parse_InvalidOrEscapingReference_RemainsMissing(string value)
    {
        var parsed = CombatStrategyResolver.Parse(value);
        Assert.False(parsed.Exists);
        Assert.False(CombatStrategyResolver.Parse(parsed.Path).Exists);
        Assert.Throws<FileNotFoundException>(() => CombatStrategyResolver.Resolve(value));
    }

    /// <summary>对外保留的 JS 换算函数同步支持 JSON 名称与键鼠前缀。</summary>
    [Fact]
    public void ResolveStrategyPath_DelegatesToUnifiedResolver()
    {
        var json = _files.Fight("仅JSON.json", "{}");
        Assert.Equal((json, "json"), AutoFightParam.ResolveStrategyPath(Path.Combine(_files.Prefix, "仅JSON")));
        var macro = _files.Macro("回放.json");
        Assert.Equal((macro, "keymouse"), AutoFightParam.ResolveStrategyPath("keymouse:" + Path.Combine(_files.Prefix, "回放")));
    }

    /// <summary>前缀的目录/文件类型错误不能在重复解析时变成其他可执行策略。</summary>
    [Fact]
    public void Parse_PrefixWithWrongFileSystemType_RemainsMissing()
    {
        _files.Fight("策略.txt");
        _files.Macro("文件夹.json\\说明.md", "");
        foreach (var value in new[] { "auto:" + Path.Combine(_files.Prefix, "策略.txt"), "keymouse:" + Path.Combine(_files.Prefix, "文件夹.json") })
        {
            var parsed = CombatStrategyResolver.Parse(value);
            Assert.False(parsed.Exists);
            Assert.False(CombatStrategyResolver.Parse(parsed.Path).Exists);
        }
    }

    /// <summary>文件名自身包含策略扩展名时，规范值仍应能还原同一个文件。</summary>
    [Fact]
    public void Parse_FileNameContainingExtension_RoundTrips()
    {
        var txt = _files.Fight("队伍.json.txt");
        var json = _files.Fight("队伍.txt.json", "{}");
        var macro = _files.Macro("录制.json.json");
        AssertReference(txt, CombatStrategyKind.Txt, txt);
        AssertReference(json, CombatStrategyKind.Json, json);
        AssertReference(macro, CombatStrategyKind.KeyMouse, macro);
    }

    /// <summary>统一断言路径、规范值及解析幂等。</summary>
    private static void AssertReference(string? value, CombatStrategyKind kind, string path, string? canonical = null)
    {
        var result = CombatStrategyResolver.Parse(value);
        Assert.True(result.Exists);
        Assert.Equal(kind, result.Kind);
        Assert.Equal(path, result.Path, ignoreCase: true);
        if (canonical != null) Assert.Equal(canonical, result.Value);
        var again = CombatStrategyResolver.Parse(result.Path);
        Assert.Equal(result.Kind, again.Kind);
        Assert.Equal(result.Path, again.Path, ignoreCase: true);
        Assert.Equal(result.Path, CombatStrategyResolver.Parse(result.Value).Path, ignoreCase: true);
    }

    /// <summary>只清理本测试自己的目录。</summary>
    public void Dispose() => _files.Dispose();
}
