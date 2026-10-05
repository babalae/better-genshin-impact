using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using LibGit2Sharp;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Core.Script.Repositories;

/// <summary>管理远程来源注册、按资源保留的版本归档和共享资源缓存；不依赖任务页面。</summary>
public sealed partial class ScriptRepositoryStore
{
    /// <summary>Pulonia 共用的资源存储，归档、缓存和脚本工作数据随任务系统集中保存。</summary>
    public static ScriptRepositoryStore Shared { get; } = new(Global.Absolute("User/Pulonia/ScriptResources"), Global.Absolute("Repos"));
    /// <summary>资源区根目录，与更新器能够重置的 Repos 目录分开。</summary>
    public string RootDirectory { get; }
    /// <summary>用于发现已经拉取的仓库，不执行下载或订阅。</summary>
    private readonly string _repositoriesDirectory;
    /// <summary>文件式来源的单文件哈希缓存；时间和大小变化后重新读取。</summary>
    private readonly ConcurrentDictionary<string, (long Size, long Ticks, string Hash, string Generation)> _fileHashes = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>成功发布当前来源版本时通知资源消费者。</summary>
    public event EventHandler<ScriptRepositoryChangedEventArgs>? RepositoryChanged;
    /// <summary>所有文本配置使用 UTF-8 无 BOM。</summary>
    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>建立显式的资源区位置，不在构造时扫描或创建目录。</summary>
    public ScriptRepositoryStore(string rootDirectory, string repositoriesDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        _repositoriesDirectory = Path.GetFullPath(repositoriesDirectory);
        if (IsWithin(RootDirectory, _repositoriesDirectory) || IsWithin(_repositoriesDirectory, RootDirectory))
            throw new ArgumentException("资源版本区与可重置仓库目录不能相互包含。");
    }

    /// <summary>取得跨进程来源锁；更新器与读取服务使用相同的短期临界区。</summary>
    public Task<FileStream> EnterSourceAccessAsync(CancellationToken ct = default) => AcquireLockAsync("sources", ct);

    /// <summary>取得独立文件锁，不依赖线程亲和的 Mutex，等待期间支持取消。</summary>
    private async Task<FileStream> AcquireLockAsync(string key, CancellationToken ct)
    {
        var directory = Path.Combine(RootDirectory, "Locks");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Hash(key) + ".lock");
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
            { await Task.Delay(50, ct).ConfigureAwait(false); }
        }
    }

    /// <summary>原子写入配置，失败时不会覆盖旧文件。</summary>
    private static void WriteJson(string path, object value)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonConvert.SerializeObject(value, Formatting.Indented), Utf8);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>读取注册列表；损坏的注册文件不能被当作空列表覆盖。</summary>
    private List<ScriptRepositoryRegistration> ReadRegistrations()
    {
        var path = Path.Combine(RootDirectory, "repositories.json");
        return File.Exists(path)
            ? JsonConvert.DeserializeObject<List<ScriptRepositoryRegistration>>(File.ReadAllText(path, Utf8))
              ?? throw new IOException("仓库注册文件为空。") : [];
    }

    /// <summary>注册或显式重新定位来源；稳定 ID 不从文件夹名称推断。</summary>
    public async Task<ScriptRepositoryRegistration> RegisterAsync(string directory, string? name = null,
        string? repositoryId = null, CancellationToken ct = default, bool reactivate = true)
    {
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("仓库位置必须是已存在的普通目录。");
        using var gate = await AcquireLockAsync("registry", ct).ConfigureAwait(false);
        var registrations = ReadRegistrations();
        var existing = registrations.FirstOrDefault(r => repositoryId is not null ? r.Id == repositoryId
            : string.Equals(r.Directory, directory, StringComparison.OrdinalIgnoreCase));
        var registration = new ScriptRepositoryRegistration
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString("N"), Directory = directory,
            Name = name ?? existing?.Name ?? Path.GetFileName(directory),
            IsEnabled = reactivate || existing?.IsEnabled != false
        };
        if (existing is not null && existing.Directory == registration.Directory && existing.Name == registration.Name
            && existing.IsEnabled == registration.IsEnabled)
            return existing;
        if (existing is not null) registrations.Remove(existing);
        registrations.Add(registration);
        WriteJson(Path.Combine(RootDirectory, "repositories.json"), registrations);
        return registration;
    }

    /// <summary>验证用户添加的已拉取目录能提供仓库索引，然后注册或恢复该来源。</summary>
    public async Task<ScriptRepositoryRegistration> AddRepositoryAsync(string directory, CancellationToken ct = default)
    {
        directory = Path.GetFullPath(directory);
        using var sourceGate = await EnterSourceAccessAsync(ct).ConfigureAwait(false);
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("请选择已拉取的仓库根目录。");
            string index;
            if (Repository.IsValid(directory))
            {
                using var repository = new Repository(directory);
                var entry = repository.Head.Tip?.Tree["repo.json"];
                if (entry?.Mode == Mode.SymbolicLink || entry?.Target is not Blob blob)
                    throw new IOException("该 Git 仓库的当前提交没有 repo.json 索引。");
                index = blob.GetContentText();
            }
            else
            {
                var indexPath = Path.Combine(directory, "repo.json");
                if (!File.Exists(indexPath) || !Directory.Exists(Path.Combine(directory, "repo")))
                    throw new IOException("文件式仓库根目录必须包含 repo.json 和 repo 文件夹。");
                index = File.ReadAllText(indexPath);
            }
            // 添加只校验索引协议，不遍历正文，也不为仓库制作内容快照。
            if (Newtonsoft.Json.Linq.JObject.Parse(index)["indexes"] is not Newtonsoft.Json.Linq.JArray)
                throw new IOException("repo.json 缺少 indexes 资源清单。");
        }, ct).ConfigureAwait(false);
        return await RegisterAsync(directory, ct: ct).ConfigureAwait(false);
    }

    /// <summary>发现本机的 Git 与离线仓库；不会读取已订阅的 User 脚本目录。</summary>
    public async Task<IReadOnlyList<ScriptRepositoryRegistration>> GetRepositoriesAsync(CancellationToken ct = default)
    {
        using (var sourceGate = await EnterSourceAccessAsync(ct).ConfigureAwait(false))
        {
            if (Directory.Exists(_repositoriesDirectory))
            {
                foreach (var directory in Directory.EnumerateDirectories(_repositoriesDirectory))
                {
                    ct.ThrowIfCancellationRequested();
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                    if (Directory.Exists(Path.Combine(directory, ".git")) || File.Exists(Path.Combine(directory, "repo.json")))
                        await RegisterAsync(directory, ct: ct, reactivate: false).ConfigureAwait(false);
                }
            }
        }
        using var gate = await AcquireLockAsync("registry", ct).ConfigureAwait(false);
        return ReadRegistrations().Where(r => r.IsEnabled).ToArray();
    }

    /// <summary>从可选仓库移除来源，不删除本地文件、保留版本或旧任务使用的注册身份。</summary>
    public async Task RemoveRepositoryAsync(string repositoryId, CancellationToken ct = default)
    {
        using var gate = await AcquireLockAsync("registry", ct).ConfigureAwait(false);
        var registrations = ReadRegistrations();
        var index = registrations.FindIndex(r => r.Id == repositoryId);
        if (index < 0) throw new IOException("未注册的脚本仓库：" + repositoryId);
        var registration = registrations[index];
        registrations[index] = new ScriptRepositoryRegistration
        {
            Id = registration.Id, Name = registration.Name, Directory = registration.Directory, IsEnabled = false
        };
        // 禁用标记持久化后自动发现不会重新加入；用户显式添加相同目录才恢复。
        WriteJson(Path.Combine(RootDirectory, "repositories.json"), registrations);
    }

    /// <summary>读取资源类型上次选中的仓库，仅保存身份，不读取仓库索引或正文。</summary>
    public async Task<string?> GetSelectedRepositoryIdAsync(string taskType, CancellationToken ct = default)
    {
        using var gate = await AcquireLockAsync("selection", ct).ConfigureAwait(false);
        var path = Path.Combine(RootDirectory, "selection.json");
        if (!File.Exists(path)) return null;
        var selections = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path, Utf8))
                         ?? throw new IOException("仓库选择配置为空。");
        return selections.GetValueOrDefault(taskType);
    }

    /// <summary>记住 JS 或地图追踪当前选择的仓库，下次打开沿用；旧任务的资源引用不改变。</summary>
    public async Task SelectRepositoryAsync(string taskType, string repositoryId, CancellationToken ct = default)
    {
        using var gate = await AcquireLockAsync("selection", ct).ConfigureAwait(false);
        var path = Path.Combine(RootDirectory, "selection.json");
        var selections = File.Exists(path)
            ? JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path, Utf8))
              ?? throw new IOException("仓库选择配置为空。") : new();
        if (selections.GetValueOrDefault(taskType) == repositoryId) return;
        selections[taskType] = repositoryId;
        WriteJson(path, selections);
    }

    /// <summary>按稳定身份读取来源注册；旧缓存不要求来源目录仍存在。</summary>
    private async Task<ScriptRepositoryRegistration> GetRegistrationAsync(string id, CancellationToken ct)
    {
        using var gate = await AcquireLockAsync("registry", ct).ConfigureAwait(false);
        return ReadRegistrations().FirstOrDefault(r => r.Id == id)
               ?? throw new IOException("未注册的脚本仓库：" + id);
    }

    /// <summary>固定当前来源供索引和检查读取，不在浏览时复制内容或保留整个版本。</summary>
    public async Task<ScriptRepositorySnapshot> OpenCurrentAsync(string repositoryId, CancellationToken ct = default)
    {
        var registration = await GetRegistrationAsync(repositoryId, ct).ConfigureAwait(false);
        return await OpenSourceAsync(registration, null, ct).ConfigureAwait(false);
    }

    /// <summary>仅打开已确认版本；缓存或对象缺失时不能改读当前 HEAD。</summary>
    public async Task<ScriptRepositorySnapshot> OpenApprovedAsync(ScriptResourceReference reference, CancellationToken ct = default)
    {
        reference.Validate();
        var registration = await GetRegistrationAsync(reference.RepositoryId, ct).ConfigureAwait(false);
        return OpenSnapshot(registration, reference.ApprovedRevision);
    }

    /// <summary>为一个已保留版本打开独立的资源读取会话，兼容已有 Git 和整仓 ZIP 快照。</summary>
    private ScriptRepositorySnapshot OpenSnapshot(ScriptRepositoryRegistration registration, string revision)
        => new(registration, revision, Path.Combine(RootDirectory, "Snapshots", registration.Id));

    /// <summary>预览允许读取尚未保留的精确来源版本，来源不可用时不能改读最新提交。</summary>
    public async Task<ScriptRepositorySnapshot> OpenResourceReadAsync(ScriptResourceReference reference, CancellationToken ct = default)
    {
        reference.Validate();
        var registration = await GetRegistrationAsync(reference.RepositoryId, ct).ConfigureAwait(false);
        if (HasRetainedResource(registration.Id, reference.ApprovedRevision, "repo/" + reference.RelativePath))
            return OpenSnapshot(registration, reference.ApprovedRevision);
        return await OpenSourceAsync(registration, reference.ApprovedRevision, ct).ConfigureAwait(false);
    }

    /// <summary>读取会话接管来源锁；完整内容树直接从 Git 对象读取，不导入对象或复制历史。</summary>
    private async Task<ScriptRepositorySnapshot> OpenSourceAsync(ScriptRepositoryRegistration registration,
        string? revision, CancellationToken ct)
    {
        var gate = await EnterSourceAccessAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var sourcePath = registration.Directory;
                if (Repository.IsValid(sourcePath))
                {
                    var repository = new Repository(sourcePath);
                    try
                    {
                        var commit = revision is null ? repository.Head.Tip : repository.Lookup<Commit>(revision);
                        if (commit is null) throw new IOException("来源提交已不可用，请刷新资源列表；不能替换为最新内容。");
                        return new ScriptRepositorySnapshot(registration, repository, commit, gate);
                    }
                    catch { repository.Dispose(); throw; }
                }
                var inventory = ReadFileSourceInventory(sourcePath, ct);
                if (revision is not null && revision != inventory.Revision)
                    throw new IOException("文件式仓库已更新，请刷新资源列表；不能替换为最新内容。");
                return new ScriptRepositorySnapshot(registration, inventory.Revision, inventory.Files, gate,
                    (path, length, ticks, hash) => _fileHashes[path] = (length, ticks, hash, inventory.Generation));
            }, ct).ConfigureAwait(false);
        }
        catch { gate.Dispose(); throw; }
    }

    /// <summary>文件式来源只读取索引正文和文件元数据，内容指纹在预览或提取对应资源时才计算。</summary>
    private (string Revision, string Generation, IReadOnlyDictionary<string, (string PhysicalPath, string Hash, long Length, DateTime Modified)> Files)
        ReadFileSourceInventory(string sourcePath, CancellationToken ct)
    {
        if (!File.Exists(Path.Combine(sourcePath, "repo.json")) || !Directory.Exists(Path.Combine(sourcePath, "repo")))
            throw new IOException("文件式仓库必须包含 repo.json 和 repo/ 目录。");
        var files = EnumerateSourceFiles(sourcePath, ct);
        var generationPath = Path.Combine(RootDirectory, "SourceVersions", Hash(sourcePath) + ".json");
        // 跨进程使用持久化代次，ZIP 保留文件时间且内容等长时也不能复用旧哈希。
        if (!File.Exists(generationPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(generationPath)!);
            WriteJson(generationPath, Guid.NewGuid().ToString("N"));
        }
        var generation = File.ReadAllText(generationPath, Utf8);
        var indexHash = Hash(File.ReadAllBytes(Path.Combine(sourcePath, "repo.json")));
        var entries = new Dictionary<string, (string PhysicalPath, string Hash, long Length, DateTime Modified)>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            var expectedHash = _fileHashes.TryGetValue(file, out var cached)
                               && cached.Size == info.Length && cached.Ticks == info.LastWriteTimeUtc.Ticks
                               && cached.Generation == generation ? cached.Hash : string.Empty;
            var relative = Path.GetRelativePath(sourcePath, file).Replace('\\', '/');
            if (relative == "repo.json") expectedHash = indexHash;
            entries.Add(relative,
                (file, expectedHash, info.Length, info.LastWriteTimeUtc));
        }
        // 导入代次覆盖解压时同大小、同时间的替换；元数据覆盖普通本地编辑，浏览不哈希整仓正文。
        var revision = Hash("file-v2\0" + generation + "\0" + indexHash + "\n"
            + string.Join("\n", entries.Select(f => f.Key + "\0" + f.Value.Length + "\0" + f.Value.Modified.Ticks)));
        // 读取所选资源时再次核对固定元数据和已经取得的内容指纹，拒绝发布混合内容。
        return (revision, generation, entries);
    }

    /// <summary>有限扫描文件式来源；不归档 Git 元数据和界面更新标记。</summary>
    private static string[] EnumerateSourceFiles(string root, CancellationToken ct)
    {
        var files = new List<string>();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        while (pending.TryPop(out var item))
        {
            ct.ThrowIfCancellationRequested();
            if (item.Depth > 64) throw new IOException("仓库目录层级超过安全上限。");
            foreach (var path in Directory.EnumerateFileSystemEntries(item.Path))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("仓库不能包含重解析点。");
                var name = Path.GetFileName(path);
                if (name is ".git" or "repo_updated.json") continue;
                if (Directory.Exists(path)) pending.Push((path, item.Depth + 1));
                else
                {
                    if (files.Count >= 100000) throw new IOException("仓库文件数量超过安全上限。");
                    ScriptRepositorySnapshot.NormalizePath(Path.GetRelativePath(root, path));
                    files.Add(path);
                }
            }
        }
        return files.OrderBy(p => Path.GetRelativePath(root, p).Replace('\\', '/'), StringComparer.Ordinal).ToArray();
    }

    /// <summary>更新成功后发布通知；消费者错误不能把成功的仓库更新变成失败。</summary>
    public void NotifyUpdated(string directory)
    {
        try
        {
            directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            _fileHashes.Clear();
            var generationDirectory = Path.Combine(RootDirectory, "SourceVersions");
            Directory.CreateDirectory(generationDirectory);
            WriteJson(Path.Combine(generationDirectory, Hash(directory) + ".json"), Guid.NewGuid().ToString("N"));
            var registrations = GetRegisteredForNotification();
            foreach (var registration in registrations.Where(r => string.Equals(r.Directory, directory, StringComparison.OrdinalIgnoreCase)))
            {
                // 调用方持有来源锁；事件只携带版本，不在更新结束时为整仓制作快照。
                string revision;
                if (Repository.IsValid(directory))
                {
                    using var repository = new Repository(directory);
                    revision = repository.Head.Tip?.Sha ?? throw new IOException("仓库没有可用提交。");
                }
                else revision = ReadFileSourceInventory(directory, CancellationToken.None).Revision;
                if (RepositoryChanged is not { } changed) continue;
                foreach (EventHandler<ScriptRepositoryChangedEventArgs> handler in changed.GetInvocationList())
                    try { handler(this, new ScriptRepositoryChangedEventArgs(registration.Id, revision)); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    /// <summary>在更新器持有来源锁时只读取注册文件，避免重复取得来源锁。</summary>
    private IReadOnlyList<ScriptRepositoryRegistration> GetRegisteredForNotification()
    {
        using var gate = AcquireLockAsync("registry", CancellationToken.None).GetAwaiter().GetResult();
        return ReadRegistrations();
    }

    /// <summary>生成规范化、大小写固定的内容标识。</summary>
    internal static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));
    /// <summary>为原始内容生成 SHA-256。</summary>
    internal static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    /// <summary>检查完整路径是否位于指定根目录内，拒绝相邻同名前缀目录。</summary>
    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
