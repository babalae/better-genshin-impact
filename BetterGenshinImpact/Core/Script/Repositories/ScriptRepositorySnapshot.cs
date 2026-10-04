using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using LibGit2Sharp;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Core.Script.Repositories;

/// <summary>一次固定版本的仓库读取会话；Git 和文件式仓库提供相同的路径语义。</summary>
public sealed class ScriptRepositorySnapshot : IDisposable
{
    /// <summary>当前来源或旧版受保护的 Git 对象库，随读取会话释放。</summary>
    private readonly Repository? _repository;
    /// <summary>固定的 Git 内容树，不再读取当前 HEAD。</summary>
    private readonly Tree? _tree;
    /// <summary>Git 资源展示提交时间，避免用纪元时间误导用户。</summary>
    private readonly DateTime _commitTime;
    /// <summary>文件式仓库的不可变归档，随读取会话释放。</summary>
    private readonly ZipArchive? _archive;
    /// <summary>按资源保留的不可变归档，会话结束时全部释放。</summary>
    private readonly List<ZipArchive> _scopeArchives = [];
    /// <summary>已保留归档的文件入口，避免重复扫描 ZIP 目录。</summary>
    private readonly Dictionary<string, ZipArchiveEntry> _retainedFiles = new(StringComparer.Ordinal);
    /// <summary>归档明确保留的目录，包括空目录。</summary>
    private readonly HashSet<string> _retainedDirectories = new(StringComparer.Ordinal);
    /// <summary>文件式当前来源的固定清单及内容指纹。</summary>
    private readonly IReadOnlyDictionary<string, (string PhysicalPath, string Hash, long Length, DateTime Modified)>? _sourceFiles;
    /// <summary>按需读取得到的正文指纹，后续读取同一会话的文件必须保持一致。</summary>
    private readonly Dictionary<string, string> _observedFileHashes = new(StringComparer.Ordinal);
    /// <summary>向来源服务回填已读取文件的指纹，不为索引预先读取全部正文。</summary>
    private readonly Action<string, long, long, string>? _recordFileHash;
    /// <summary>当前来源会话持有更新锁，避免读取时被重置或重新克隆。</summary>
    private readonly IDisposable? _sourceAccess;
    /// <summary>列表中缺少时间时使用的来源提交或索引文件时间。</summary>
    public DateTime LastWriteTime => _commitTime;
    /// <summary>来源仓库注册。</summary>
    public ScriptRepositoryRegistration Registration { get; }
    /// <summary>本次固定的来源版本。</summary>
    public string Revision { get; }

    /// <summary>打开已经保留的来源版本，失败时不回退到最新版本。</summary>
    internal ScriptRepositorySnapshot(ScriptRepositoryRegistration registration, string revision, string directory)
    {
        Registration = registration;
        Revision = revision;
        var scopesPath = System.IO.Path.Combine(directory, revision, "scopes.json");
        if (File.Exists(scopesPath))
        {
            try
            {
                var scopes = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(scopesPath))
                             ?? throw new IOException("资源归档清单为空。");
                if (scopes.Count == 0) throw new IOException("资源归档清单没有保留的内容。");
                foreach (var scope in scopes)
                {
                    var root = NormalizePath(scope.Key);
                    if (root.Length == 0 || root != scope.Key || scope.Value is null || scope.Value.Length != 64
                        || !scope.Value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                        throw new IOException("资源归档清单包含无效路径。");
                    var archive = ZipFile.OpenRead(System.IO.Path.Combine(directory, "Archives", scope.Value + ".zip"));
                    _scopeArchives.Add(archive);
                    foreach (var entry in archive.Entries)
                    {
                        var isDirectory = entry.FullName.EndsWith('/');
                        var path = NormalizePath(isDirectory ? entry.FullName.TrimEnd('/') : entry.FullName);
                        if (path != root && !path.StartsWith(root + "/", StringComparison.Ordinal))
                            throw new IOException("归档文件超出声明的资源范围。");
                        if (isDirectory) _retainedDirectories.Add(path);
                        else _retainedFiles.TryAdd(path, entry);
                    }
                }
                return;
            }
            catch { Dispose(); throw; }
        }
        var archivePath = System.IO.Path.Combine(directory, revision + ".zip");
        if (File.Exists(archivePath))
            _archive = ZipFile.OpenRead(archivePath);
        else
        {
            _repository = new Repository(System.IO.Path.Combine(directory, "objects.git"));
            try
            {
                var commit = _repository.Lookup<Commit>("refs/pulonia/revisions/" + revision)
                             ?? throw new IOException("所需仓库旧版本不存在，不能替换为最新内容。");
                _tree = commit.Tree;
                _commitTime = commit.Committer.When.UtcDateTime;
            }
            catch { _repository.Dispose(); throw; }
        }
    }

    /// <summary>直接打开一个固定提交；来源锁由读取会话接管，不复制 Git 对象。</summary>
    internal ScriptRepositorySnapshot(ScriptRepositoryRegistration registration, Repository repository, Commit commit, IDisposable sourceAccess)
    {
        Registration = registration;
        Revision = commit.Sha;
        _repository = repository;
        _tree = commit.Tree;
        _commitTime = commit.Committer.When.UtcDateTime;
        _sourceAccess = sourceAccess;
    }

    /// <summary>直接打开一个文件式来源的固定清单；每次读取正文会复核对应指纹。</summary>
    internal ScriptRepositorySnapshot(ScriptRepositoryRegistration registration, string revision,
        IReadOnlyDictionary<string, (string PhysicalPath, string Hash, long Length, DateTime Modified)> files,
        IDisposable sourceAccess, Action<string, long, long, string> recordFileHash)
    {
        Registration = registration;
        Revision = revision;
        _sourceFiles = files;
        _sourceAccess = sourceAccess;
        _recordFileHash = recordFileHash;
        _commitTime = files.TryGetValue("repo.json", out var index) ? index.Modified : DateTime.UnixEpoch;
    }

    /// <summary>统一仓库相对路径并拒绝 Windows 特殊路径、父级跳转和链接逃逸。</summary>
    public static string NormalizePath(string path)
    {
        if (path is null || path.Length > 2000 || System.IO.Path.IsPathRooted(path))
            throw new ArgumentException("仓库路径必须是相对路径。");
        path = path.Replace('\\', '/');
        while (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];
        if (path is "" or ".") return string.Empty;
        var segments = path.Split('/');
        if (segments.Any(part => part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
            || part.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0
            || System.Text.RegularExpressions.Regex.IsMatch(part, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\\.|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            throw new ArgumentException("仓库路径包含不安全或无法提取的名称。");
        return string.Join('/', segments);
    }

    /// <summary>查找固定 Git 树中的路径，不允许通过符号链接读取内容。</summary>
    private TreeEntry? GetGitEntry(string path)
    {
        var entry = _tree?[path];
        if (entry?.Mode == Mode.SymbolicLink || entry?.Mode == Mode.GitLink)
            throw new IOException("仓库资源不能是符号链接或子模块。");
        return entry;
    }

    /// <summary>检查一个固定版本中的普通文件是否存在。</summary>
    public bool FileExists(string path)
    {
        path = NormalizePath(path);
        if (_sourceFiles is not null) return _sourceFiles.ContainsKey(path);
        if (_scopeArchives.Count > 0) return _retainedFiles.ContainsKey(path);
        return _archive is not null ? _archive.GetEntry(path) is not null
            : GetGitEntry(path)?.TargetType == TreeEntryTargetType.Blob;
    }

    /// <summary>检查固定版本中的资源目录是否存在。</summary>
    public bool DirectoryExists(string path)
    {
        path = NormalizePath(path);
        if (_sourceFiles is not null) return path.Length == 0 || _sourceFiles.Keys.Any(p => p.StartsWith(path + "/", StringComparison.Ordinal));
        if (_scopeArchives.Count > 0) return path.Length == 0 || _retainedDirectories.Contains(path)
            || _retainedFiles.Keys.Any(p => p.StartsWith(path + "/", StringComparison.Ordinal));
        return _archive is not null ? path.Length == 0 || _archive.Entries.Any(e => e.FullName.StartsWith(path + "/", StringComparison.Ordinal))
            : path.Length == 0 || GetGitEntry(path)?.TargetType == TreeEntryTargetType.Tree;
    }

    /// <summary>读取原始文件字节；缺失文件必须明确失败。</summary>
    public byte[] ReadBytes(string path)
    {
        using var stream = OpenRead(path);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>打开固定版本文件流，归档过程直接流式复制，避免整份资源驻留内存。</summary>
    internal Stream OpenRead(string path)
    {
        path = NormalizePath(path);
        if (_sourceFiles is not null)
        {
            var entry = _sourceFiles.TryGetValue(path, out var file) ? file : throw new FileNotFoundException("仓库资源不存在：" + path);
            VerifySourceMetadata(entry);
            var bytes = File.ReadAllBytes(entry.PhysicalPath);
            VerifySourceMetadata(entry);
            var hash = ScriptRepositoryStore.Hash(bytes);
            var expected = entry.Hash.Length > 0 ? entry.Hash : _observedFileHashes.GetValueOrDefault(path);
            if (expected is not null && expected != hash) throw new IOException("读取期间文件式来源变化，请刷新资源列表。");
            _observedFileHashes[path] = hash;
            _recordFileHash?.Invoke(entry.PhysicalPath, entry.Length, entry.Modified.Ticks, hash);
            return new MemoryStream(bytes, false);
        }
        if (_scopeArchives.Count > 0)
            return (_retainedFiles.GetValueOrDefault(path) ?? throw new FileNotFoundException("已保留版本中不存在资源：" + path)).Open();
        return _archive is not null
            ? (_archive.GetEntry(path) ?? throw new FileNotFoundException("仓库资源不存在：" + path)).Open()
            : (GetGitEntry(path)?.Target as Blob ?? throw new FileNotFoundException("仓库资源不存在：" + path)).GetContentStream();
    }

    /// <summary>确认文件式来源仍符合打开会话时的固定元数据，外部写入不得混进批准内容。</summary>
    private static void VerifySourceMetadata((string PhysicalPath, string Hash, long Length, DateTime Modified) entry)
    {
        var info = new FileInfo(entry.PhysicalPath);
        if (!info.Exists || info.Length != entry.Length || info.LastWriteTimeUtc != entry.Modified
            || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("读取期间文件式来源变化，请刷新资源列表。");
    }

    /// <summary>获取单个内容块的身份，Git 只读对象 ID，文件来源仅哈希本次保留的资源范围。</summary>
    internal string GetContentIdentity(string path)
    {
        path = NormalizePath(path);
        if (_tree is not null) return GetGitEntry(path)?.Target.Id.Sha ?? throw new FileNotFoundException("仓库资源不存在：" + path);
        if (_sourceFiles is not null)
        {
            var values = _sourceFiles.Where(p => p.Key == path || p.Key.StartsWith(path + "/", StringComparison.Ordinal))
                .OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "\0"
                    + (p.Value.Hash.Length > 0 ? p.Value.Hash : _observedFileHashes.GetValueOrDefault(p.Key)
                       ?? ScriptRepositoryStore.Hash(ReadBytes(p.Key))));
            return ScriptRepositoryStore.Hash(string.Join("\n", values));
        }
        throw new InvalidOperationException("已保留归档不需要重新生成来源块身份。");
    }

    /// <summary>按 BOM 自动识别文本，与现有脚本文件读取语义一致。</summary>
    public string ReadText(string path)
    {
        using var stream = new MemoryStream(ReadBytes(path));
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    /// <summary>有限枚举普通文件，不读取正文；过深或超出上限时拒绝返回部分结果。</summary>
    public IReadOnlyList<ScriptRepositoryEntry> EnumerateFiles(string path, int limit = 100000)
    {
        path = NormalizePath(path);
        var result = new List<ScriptRepositoryEntry>();
        if (_sourceFiles is not null)
        {
            foreach (var item in _sourceFiles.Where(p => path.Length == 0 || p.Key.StartsWith(path + "/", StringComparison.Ordinal)))
            {
                if (result.Count >= limit) throw new IOException("仓库文件数量超过安全上限。");
                result.Add(new ScriptRepositoryEntry(item.Key, item.Value.Length, item.Value.Modified));
            }
        }
        else if (_scopeArchives.Count > 0)
        {
            if (!DirectoryExists(path)) throw new DirectoryNotFoundException("已保留版本中不存在目录：" + path);
            foreach (var item in _retainedFiles.Where(p => path.Length == 0 || p.Key.StartsWith(path + "/", StringComparison.Ordinal)))
            {
                if (result.Count >= limit) throw new IOException("仓库文件数量超过安全上限。");
                result.Add(new ScriptRepositoryEntry(item.Key, item.Value.Length, item.Value.LastWriteTime.UtcDateTime));
            }
        }
        else if (_archive is not null)
        {
            foreach (var item in _archive.Entries.Where(e => !e.FullName.EndsWith('/')
                && (path.Length == 0 || e.FullName.StartsWith(path + "/", StringComparison.Ordinal))))
            {
                if (result.Count >= limit) throw new IOException("仓库文件数量超过安全上限。");
                result.Add(new ScriptRepositoryEntry(NormalizePath(item.FullName), item.Length, item.LastWriteTime.UtcDateTime));
            }
        }
        else
        {
            var tree = path.Length == 0 ? _tree : GetGitEntry(path)?.Target as Tree;
            if (tree is null) throw new DirectoryNotFoundException("仓库目录不存在：" + path);
            Visit(tree, path, 0);
        }
        return result.OrderBy(e => e.Path, StringComparer.Ordinal).ToArray();

        // Git 对象仅在会话内使用，返回 DTO 不能携带已经释放的原生对象。
        void Visit(Tree tree, string prefix, int depth)
        {
            if (depth > 64) throw new IOException("仓库目录层级超过安全上限。");
            foreach (var entry in tree)
            {
                var name = NormalizePath((prefix.Length == 0 ? "" : prefix + "/") + entry.Name);
                if (entry.Mode is Mode.SymbolicLink or Mode.GitLink) continue;
                if (entry.Target is Tree child) Visit(child, name, depth + 1);
                else if (entry.Target is Blob blob)
                {
                    if (result.Count >= limit) throw new IOException("仓库文件数量超过安全上限。");
                    result.Add(new ScriptRepositoryEntry(name, blob.Size, _commitTime));
                }
            }
        }
    }

    /// <summary>释放读取会话拥有的 Git 或归档资源。</summary>
    public void Dispose()
    {
        foreach (var archive in _scopeArchives) archive.Dispose();
        _archive?.Dispose();
        _repository?.Dispose();
        _sourceAccess?.Dispose();
    }
}
