using System;
using System.IO;
using System.Linq;

namespace BetterGenshinImpact.Core.Script.Repositories;

/// <summary>JS 宿主内部资源读取上下文，所有路线查询都固定到任务已确认的同一仓库版本。</summary>
public sealed class ScriptRepositoryResourceContext(ScriptRepositorySnapshot snapshot) : IDisposable
{
    /// <summary>本次执行拥有的来源读取会话。</summary>
    private readonly ScriptRepositorySnapshot _snapshot = snapshot;
    /// <summary>串行化脚本并行发起的原生 Git/Zip 读取。</summary>
    private readonly object _gate = new();

    /// <summary>将旧 User/AutoPathing 相对路径映射到已确认来源中的 pathing/ 目录。</summary>
    private static string Resolve(string path)
    {
        var relative = ScriptRepositorySnapshot.NormalizePath(path);
        return "repo/pathing" + (relative.Length == 0 ? "" : "/" + relative);
    }

    /// <summary>从已确认来源读取路线正文，缺失时明确失败。</summary>
    public string ReadPathingText(string path) { lock (_gate) return _snapshot.ReadText(Resolve(path)); }
    /// <summary>检查已确认来源中的路线文件。</summary>
    public bool IsPathingFile(string path) { lock (_gate) return _snapshot.FileExists(Resolve(path)); }
    /// <summary>检查已确认来源中的路线目录。</summary>
    public bool IsPathingDirectory(string path) { lock (_gate) return _snapshot.DirectoryExists(Resolve(path)); }
    /// <summary>查询目录的一级文件和目录，返回相对 pathing/ 根目录的位置。</summary>
    public string[] ReadPathingDirectory(string path)
    {
        lock (_gate)
        {
            var root = Resolve(path);
            if (!_snapshot.DirectoryExists(root)) throw new DirectoryNotFoundException("来源版本中不存在路线目录：" + path);
            return _snapshot.EnumerateFiles(root).Select(entry =>
            {
                var suffix = entry.Path[(root.Length + 1)..];
                var slash = suffix.IndexOf('/');
                return (root + "/" + (slash < 0 ? suffix : suffix[..slash]))["repo/pathing/".Length..];
            }).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        }
    }

    /// <summary>脚本所有操作退出后释放来源会话。</summary>
    public void Dispose() { lock (_gate) _snapshot.Dispose(); }
}
