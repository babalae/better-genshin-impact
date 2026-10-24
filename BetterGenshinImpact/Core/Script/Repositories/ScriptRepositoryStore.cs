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
using BetterGenshinImpact.Helpers;
using LibGit2Sharp;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Core.Script.Repositories;

/// <summary>
/// 管理远程来源注册、按资源保留的版本归档、JS 工作目录与预览；不依赖任务页面。
/// 目录名和文件名只使用短 ID 与可读名称，哈希和来源修订只保存在 JSON 中，避免 Windows 路径过长。
/// </summary>
public sealed partial class ScriptRepositoryStore
{
    /// <summary>Pulonia 共用的资源存储，归档和脚本工作数据随任务系统集中保存。</summary>
    public static ScriptRepositoryStore Shared { get; } = new(Global.Absolute("User/Pulonia/ScriptResources"), Global.Absolute("Repos"));
    /// <summary>资源区根目录，与更新器能够重置的 Repos 目录分开。</summary>
    public string RootDirectory { get; }
    /// <summary>托管 clone 目录根位置，仓库注册本身不执行下载或订阅。</summary>
    private readonly string _repositoriesDirectory;
    /// <summary>
    /// 文件式来源的单文件哈希缓存；大小、时间或导入标记变化后重新读取。
    /// 导入标记来自仓库根目录 repo_updated.json 的写入时间，其他实例导入 ZIP 后本实例的旧缓存也会失效。
    /// </summary>
    private readonly ConcurrentDictionary<string, (long Size, long Ticks, string Hash, long ImportStamp)> _fileHashes = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>成功发布当前来源版本时通知资源消费者。</summary>
    public event EventHandler<ScriptRepositoryChangedEventArgs>? RepositoryChanged;
    /// <summary>所有文本配置使用 UTF-8 无 BOM。</summary>
    private static readonly UTF8Encoding Utf8 = new(false, true);
    /// <summary>ZIP 导入和 Git 更新每次都会重写的更新标记文件，位于仓库根目录。</summary>
    private const string ImportMarkerFileName = "repo_updated.json";
    /// <summary>
    /// JS 工作目录锁的最长等待时间。另一个运行（可能来自其他 BetterGI 实例）占用同一脚本时，
    /// 超时后明确报错，而不是无限轮询、让本次运行静默挂起。
    /// </summary>
    internal TimeSpan WorkspaceLockTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>建立显式的资源区位置，不在构造时扫描或创建目录。</summary>
    public ScriptRepositoryStore(string rootDirectory, string repositoriesDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        _repositoriesDirectory = Path.GetFullPath(repositoriesDirectory);
        if (IsWithin(RootDirectory, _repositoriesDirectory) || IsWithin(_repositoriesDirectory, RootDirectory))
            throw new ArgumentException("资源版本区与可重置仓库目录不能相互包含。");
    }

    /// <summary>清理启动前遗留且当前未被其他实例持有的跨进程锁文件。</summary>
    public int CleanupStaleLockFiles()
    {
        var directory = Path.Combine(RootDirectory, "Locks");
        string[] paths;
        try
        {
            if (!Directory.Exists(directory)) return 0;
            paths = Directory.GetFiles(directory, "*.lock", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 锁目录不可访问不应阻止 BetterGI 启动，后续取得锁时仍会报告真实错误。
            return 0;
        }

        var removed = 0;
        // Locks 目录只由本存储写入，目录下的全部 *.lock 都按可读锁名处理。
        foreach (var path in paths)
        {
            try
            {
                // 独占打开并在关闭句柄时原子删除；其他实例持有或等待同名锁时不会形成两个锁文件。
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1,
                           FileOptions.DeleteOnClose)) { }
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 活跃锁或瞬时文件竞争必须保留，不能影响其他 BetterGI 实例。
            }
        }
        return removed;
    }

    /// <summary>
    /// 取得一个来源目录的跨进程锁；更新器与读取服务按目录使用相同的短期临界区。
    /// 锁只覆盖该目录，同步一个仓库不会阻塞浏览或读取另一个仓库。
    /// </summary>
    /// <param name="directory">来源仓库的本机目录</param>
    /// <param name="ct">等待期间的取消令牌</param>
    public Task<FileStream> EnterSourceAccessAsync(string directory, CancellationToken ct = default)
        => AcquireLockAsync(GetSourceLockName(directory), ct);

    /// <summary>由规范化的完整目录派生来源锁名 <c>sources.&lt;16 位&gt;</c>，Windows 路径不区分大小写。</summary>
    private static string GetSourceLockName(string directory)
        => "sources." + ShortId.FromText(string.Empty,
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)).ToUpperInvariant());

    /// <summary>
    /// 取得 <c>Locks\&lt;名称&gt;.lock</c> 跨进程锁。名称为 sources.&lt;目录键&gt;、registry 或 retain.&lt;仓库ID&gt;。
    /// registry 只保护注册列表的读改写，读取注册不取锁。
    /// 锁顺序固定为：工作目录锁 → retain → sources → registry，持有后者时不再取得前者。
    /// </summary>
    private Task<FileStream> AcquireLockAsync(string name, CancellationToken ct)
    {
        var directory = Path.Combine(RootDirectory, "Locks");
        Directory.CreateDirectory(directory);
        return AcquireFileLockAsync(Path.Combine(directory, name + ".lock"), ct);
    }

    /// <summary>
    /// 独占打开指定锁文件，不依赖线程亲和的 Mutex，等待期间支持取消。
    /// 指定超时时间后，超时抛出带说明的 <see cref="IOException"/>；不指定时一直等待到取得锁或取消。
    /// </summary>
    /// <param name="path">锁文件路径</param>
    /// <param name="ct">等待期间的取消令牌</param>
    /// <param name="timeout">最长等待时间；为 null 时不限时</param>
    /// <param name="timeoutMessage">超时异常的说明文字</param>
    private static async Task<FileStream> AcquireFileLockAsync(string path, CancellationToken ct,
        TimeSpan? timeout = null, string? timeoutMessage = null)
    {
        var deadline = timeout is { } limit ? Environment.TickCount64 + (long)limit.TotalMilliseconds : (long?)null;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
            {
                if (deadline is { } end && Environment.TickCount64 >= end)
                    throw new IOException(timeoutMessage ?? "等待资源锁超时。", ex);
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 原子写入配置，失败时不会覆盖旧文件。
    /// 目标已存在时用 <see cref="File.Replace(string, string, string?)"/> 替换：读取方以 FileShare.Delete 打开时也能完成替换，
    /// 读取方继续读完旧版本；配置类数据在多个写入方之间按后写者胜出。
    /// </summary>
    private static void WriteJson(string path, object value)
    {
        // 临时名与目标名无关，不让长文件名再叠加一段随机后缀。
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, ShortId.NewTemporaryName());
        try
        {
            File.WriteAllText(temporary, JsonConvert.SerializeObject(value, Formatting.Indented), Utf8);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>读取一份配置文本；允许写入方在读取期间替换文件，已打开的句柄仍读取原版本。</summary>
    /// <param name="path">配置文件路径</param>
    internal static string ReadText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>读取注册列表；损坏的注册文件不能被当作空列表覆盖。读取不取锁，写入以原子替换发布。</summary>
    private List<ScriptRepositoryRegistration> ReadRegistrations()
    {
        var path = Path.Combine(RootDirectory, "repositories.json");
        return File.Exists(path)
            ? JsonConvert.DeserializeObject<List<ScriptRepositoryRegistration>>(ReadText(path))
              ?? throw new IOException("仓库注册文件为空。") : [];
    }

    /// <summary>生成未被已有注册占用的仓库 ID（<c>rep_</c> + 16 位）；调用方须持有 registry 锁。</summary>
    private static string NewRepositoryId(IReadOnlyCollection<ScriptRepositoryRegistration> registrations)
    {
        while (true)
        {
            var id = ShortId.Create("rep");
            if (registrations.All(r => r.Id != id)) return id;
        }
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
        if (existing?.IsOfficial == true)
        {
            if (!string.Equals(directory, OfficialDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("官方仓库目录固定，不能重新定位。");
            return existing;
        }
        if (existing?.Kind == "remote" && !string.Equals(existing.Directory, directory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("远程仓库的 clone 目录固定，不能重新定位。");
        if (string.Equals(directory, OfficialDirectory, StringComparison.OrdinalIgnoreCase))
        {
            var official = CreateOfficialRegistration();
            registrations.RemoveAll(r => r.Id == OfficialRepositoryId);
            registrations.Add(official);
            WriteJson(Path.Combine(RootDirectory, "repositories.json"), registrations);
            return official;
        }
        var registration = new ScriptRepositoryRegistration
        {
            Id = existing?.Id ?? NewRepositoryId(registrations), Directory = directory,
            Name = name ?? existing?.Name ?? Path.GetFileName(directory),
            IsEnabled = reactivate || existing?.IsEnabled != false,
            Kind = existing?.Kind ?? "local", Remotes = existing?.Remotes ?? [],
            ActiveRemoteId = existing?.ActiveRemoteId, Branch = existing?.Branch ?? "release"
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
        using var sourceGate = await EnterSourceAccessAsync(directory, ct).ConfigureAwait(false);
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

    /// <summary>列出明确注册的仓库并补入内置官方来源；不会读取已订阅的 User 脚本目录。</summary>
    public async Task<IReadOnlyList<ScriptRepositoryRegistration>> GetRepositoriesAsync(CancellationToken ct = default)
    {
        await EnsureOfficialRepositoryAsync(ct).ConfigureAwait(false);
        return ReadRegistrations().Where(r => r.IsEnabled).OrderByDescending(r => r.IsOfficial).ToArray();
    }

    /// <summary>从可选仓库移除来源，不删除本地文件、保留版本或旧任务使用的注册身份。</summary>
    public async Task RemoveRepositoryAsync(string repositoryId, CancellationToken ct = default)
    {
        using var gate = await AcquireLockAsync("registry", ct).ConfigureAwait(false);
        var registrations = ReadRegistrations();
        var index = registrations.FindIndex(r => r.Id == repositoryId);
        if (index < 0) throw new IOException("未注册的脚本仓库：" + repositoryId);
        var registration = registrations[index];
        if (registration.IsOfficial) throw new InvalidOperationException("官方仓库不能移除或禁用。");
        registrations[index] = new ScriptRepositoryRegistration
        {
            Id = registration.Id, Name = registration.Name, Directory = registration.Directory, IsEnabled = false,
            Kind = registration.Kind, Remotes = registration.Remotes,
            ActiveRemoteId = registration.ActiveRemoteId, Branch = registration.Branch
        };
        // 禁用标记持久化后自动发现不会重新加入；用户显式添加相同目录才恢复。
        WriteJson(Path.Combine(RootDirectory, "repositories.json"), registrations);
    }

    /// <summary>读取资源类型上次选中的仓库，仅保存身份，不读取仓库索引或正文。</summary>
    public Task<string?> GetSelectedRepositoryIdAsync(string taskType, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ReadSelections().GetValueOrDefault(taskType));
    }

    /// <summary>
    /// 记住 JS 或地图追踪当前选择的仓库，下次打开沿用；旧任务的资源引用不改变。
    /// 偏好属于配置类数据，多个实例同时修改时后写者胜出，不取锁。
    /// </summary>
    public Task SelectRepositoryAsync(string taskType, string repositoryId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var selections = ReadSelections();
        if (selections.GetValueOrDefault(taskType) == repositoryId) return Task.CompletedTask;
        selections[taskType] = repositoryId;
        Directory.CreateDirectory(RootDirectory);
        WriteJson(Path.Combine(RootDirectory, "selection.json"), selections);
        return Task.CompletedTask;
    }

    /// <summary>读取按资源类型保存的仓库选择；文件不存在时返回空表，损坏时明确失败。</summary>
    private Dictionary<string, string> ReadSelections()
    {
        var path = Path.Combine(RootDirectory, "selection.json");
        return File.Exists(path)
            ? JsonConvert.DeserializeObject<Dictionary<string, string>>(ReadText(path))
              ?? throw new IOException("仓库选择配置为空。") : new();
    }

    /// <summary>按稳定身份读取来源注册；旧缓存不要求来源目录仍存在。读取不取 registry 锁。</summary>
    private ScriptRepositoryRegistration GetRegistration(string id)
        => ReadRegistrations().FirstOrDefault(r => r.Id == id) ?? throw new IOException("未注册的脚本仓库：" + id);

    /// <summary>固定当前来源供索引和检查读取，不在浏览时复制内容或保留整个版本。</summary>
    public Task<ScriptRepositorySnapshot> OpenCurrentAsync(string repositoryId, CancellationToken ct = default)
        => OpenSourceAsync(GetRegistration(repositoryId), null, ct);

    /// <summary>仅打开已确认版本；缓存或对象缺失时不能改读当前 HEAD。</summary>
    public Task<ScriptRepositorySnapshot> OpenApprovedAsync(ScriptResourceReference reference, CancellationToken ct = default)
    {
        reference.Validate();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(OpenSnapshot(GetRegistration(reference.RepositoryId), reference.ApprovedRevision));
    }

    /// <summary>为一个已保留版本打开独立的资源读取会话，内容来自仓库归档目录中的归档块。</summary>
    private ScriptRepositorySnapshot OpenSnapshot(ScriptRepositoryRegistration registration, string revision)
        => new(registration, revision, Path.Combine(RootDirectory, "Snapshots", registration.Id));

    /// <summary>预览允许读取尚未保留的精确来源版本，来源不可用时不能改读最新提交。</summary>
    public async Task<ScriptRepositorySnapshot> OpenResourceReadAsync(ScriptResourceReference reference, CancellationToken ct = default)
    {
        reference.Validate();
        var registration = GetRegistration(reference.RepositoryId);
        if (HasRetainedResource(registration.Id, reference.ApprovedRevision, "repo/" + reference.RelativePath))
            return OpenSnapshot(registration, reference.ApprovedRevision);
        return await OpenSourceAsync(registration, reference.ApprovedRevision, ct).ConfigureAwait(false);
    }

    /// <summary>读取会话接管该来源目录的锁；完整内容树直接从 Git 对象读取，不导入对象或复制历史。</summary>
    private async Task<ScriptRepositorySnapshot> OpenSourceAsync(ScriptRepositoryRegistration registration,
        string? revision, CancellationToken ct)
    {
        var gate = await EnterSourceAccessAsync(registration.Directory, ct).ConfigureAwait(false);
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
                    (path, length, ticks, hash) => _fileHashes[path] = (length, ticks, hash, inventory.ImportStamp));
            }, ct).ConfigureAwait(false);
        }
        catch { gate.Dispose(); throw; }
    }

    /// <summary>
    /// 文件式来源只读取索引正文和文件元数据，内容指纹在预览或提取对应资源时才计算。
    /// 修订由导入标记、索引正文和全部文件的大小与时间决定，各实例对同一磁盘状态得到同一修订。
    /// 暂不处理：没有重新导入、且替换后大小与时间都不变的手工编辑，会沿用旧修订。
    /// </summary>
    /// <param name="sourcePath">文件式来源的根目录</param>
    /// <param name="ct">扫描期间的取消令牌</param>
    private (string Revision, long ImportStamp, IReadOnlyDictionary<string, (string PhysicalPath, string Hash, long Length, DateTime Modified)> Files)
        ReadFileSourceInventory(string sourcePath, CancellationToken ct)
    {
        if (!File.Exists(Path.Combine(sourcePath, "repo.json")) || !Directory.Exists(Path.Combine(sourcePath, "repo")))
            throw new IOException("文件式仓库必须包含 repo.json 和 repo/ 目录。");
        var files = EnumerateSourceFiles(sourcePath, ct);
        // ZIP 导入每次都会重写根目录的更新标记；把它的写入时间作为导入标记，
        // 解压后同大小、同时间的替换也会得到新修订并使其他实例的哈希缓存失效，不需要另存导入代次。
        var marker = new FileInfo(Path.Combine(sourcePath, ImportMarkerFileName));
        var importStamp = marker.Exists ? marker.LastWriteTimeUtc.Ticks : 0;
        var indexHash = Hash(File.ReadAllBytes(Path.Combine(sourcePath, "repo.json")));
        var entries = new Dictionary<string, (string PhysicalPath, string Hash, long Length, DateTime Modified)>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            var expectedHash = _fileHashes.TryGetValue(file, out var cached)
                               && cached.Size == info.Length && cached.Ticks == info.LastWriteTimeUtc.Ticks
                               && cached.ImportStamp == importStamp ? cached.Hash : string.Empty;
            var relative = Path.GetRelativePath(sourcePath, file).Replace('\\', '/');
            if (relative == "repo.json") expectedHash = indexHash;
            entries.Add(relative,
                (file, expectedHash, info.Length, info.LastWriteTimeUtc));
        }
        // 导入标记覆盖解压时同大小、同时间的替换；元数据覆盖普通本地编辑，浏览不哈希整仓正文。
        var revision = Hash("file-v3\0" + importStamp + "\0" + indexHash + "\n"
            + string.Join("\n", entries.Select(f => f.Key + "\0" + f.Value.Length + "\0" + f.Value.Modified.Ticks)));
        // 读取所选资源时再次核对固定元数据和已经取得的内容指纹，拒绝发布混合内容。
        return (revision, importStamp, entries);
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
                if (name is ".git" or ImportMarkerFileName) continue;
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

    /// <summary>
    /// 更新、导入 ZIP 或刷新本地目录成功后发布通知；消费者错误不能把成功的仓库更新变成失败。
    /// 同时清空本实例的单文件哈希缓存，随后读取一定按新正文重新计算。
    /// </summary>
    public void NotifyUpdated(string directory)
    {
        try
        {
            directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            _fileHashes.Clear();
            foreach (var registration in ReadRegistrations().Where(r => string.Equals(r.Directory, directory, StringComparison.OrdinalIgnoreCase)))
            {
                // 调用方持有来源锁；事件只携带版本，不在更新结束时为整仓制作快照。
                string revision;
                if (Repository.IsValid(directory))
                {
                    using var repository = new Repository(directory);
                    revision = repository.Head.Tip?.Sha ?? throw new IOException("仓库没有可用提交。");
                }
                else revision = ReadFileSourceInventory(registration.Directory, CancellationToken.None).Revision;
                if (RepositoryChanged is not { } changed) continue;
                foreach (EventHandler<ScriptRepositoryChangedEventArgs> handler in changed.GetInvocationList())
                    try { handler(this, new ScriptRepositoryChangedEventArgs(registration.Id, revision)); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    /// <summary>生成规范化、大小写固定的内容标识。</summary>
    internal static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));
    /// <summary>为原始内容生成 SHA-256；用于来源修订、归档块身份和读取校验，不是任务资源指纹。</summary>
    internal static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    /// <summary>检查完整路径是否位于指定根目录内，拒绝相邻同名前缀目录。</summary>
    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
