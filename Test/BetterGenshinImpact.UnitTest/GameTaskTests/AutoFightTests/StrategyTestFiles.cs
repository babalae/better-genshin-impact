using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFightTests;

/// <summary>每个测试独占子目录，不修改全局启动路径或用户已有策略。</summary>
internal sealed class StrategyTestFiles : IDisposable
{
    /// <summary>本次测试独占的相对目录名。</summary>
    public string Prefix { get; } = "__策略测试_" + Guid.NewGuid().ToString("N");
    /// <summary>测试拥有的外部临时目录。</summary>
    private readonly string _external = Path.Combine(Path.GetTempPath(), "BetterGI-StrategyTests", Guid.NewGuid().ToString("N"));
    /// <summary>已创建的测试目录，仅删除这些目录。</summary>
    private readonly List<string> _owned = [];

    /// <summary>写入文本或 JSON 战斗策略并返回绝对路径。</summary>
    public string Fight(string relative, string content = "钟离 e")
        => Write(Global.Absolute(Path.Combine("User", "AutoFight", Prefix)), relative, content);
    /// <summary>写入键鼠宏。</summary>
    public string Macro(string relative, string content = "{\"macroEvents\":[]}")
        => Write(Global.Absolute(Path.Combine("User", "KeyMouseScript", Prefix)), relative, content);
    /// <summary>写入不位于两种策略根目录内的 JSON 文件。</summary>
    public string External(string relative, string content) => Write(_external, relative, content);

    /// <summary>只在测试拥有的子目录内创建文件。</summary>
    private string Write(string root, string relative, string content)
    {
        root = Path.GetFullPath(root);
        if (!_owned.Contains(root)) _owned.Add(root);
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>清理本测试创建的唯一目录，保留根目录和所有其他测试/用户文件。</summary>
    public void Dispose()
    {
        foreach (var root in _owned)
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

/// <summary>文件目录测试串行运行，防止一个测试清理目录时另一个仍在扫描。</summary>
[CollectionDefinition("CombatStrategyFiles", DisableParallelization = true)]
public sealed class CombatStrategyFilesCollection { }
