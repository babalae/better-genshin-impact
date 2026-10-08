using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Core.Script.Repositories;

/// <summary>仓库管理的注册操作，官方保护和固定目录约束同时在服务层执行。</summary>
public sealed partial class ScriptRepositoryStore
{
    /// <summary>保留的官方仓库身份，重置或切换镜像时始终保持不变。</summary>
    public const string OfficialRepositoryId = "00000000000000000000000000000001";

    /// <summary>与原官方仓库弹窗共用的固定 clone 目录。</summary>
    public string OfficialDirectory => Path.Combine(_repositoriesDirectory, "bettergi-scripts-list");

    /// <summary>按稳定身份取得唯一的第三方 clone 位置，配置不能重定向到另一个仓库。</summary>
    public string GetManagedRemoteDirectory(string repositoryId)
    {
        if (repositoryId.Length != 32 || !repositoryId.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new ArgumentException("仓库 ID 无效。");
        return Path.Combine(_repositoriesDirectory, "Pulonia", repositoryId);
    }

    /// <summary>构造内置官方记录，即使尚未下载也可在选择器中显示。</summary>
    private ScriptRepositoryRegistration CreateOfficialRegistration() => new()
    {
        Id = OfficialRepositoryId, Name = "官方脚本仓库", Kind = "official", Directory = OfficialDirectory,
        Remotes = ScriptRepoUpdater.RepoChannels.Select(pair => new ScriptRepositoryRemote
            { Id = pair.Key, Name = pair.Key, Url = pair.Value }).ToArray(), Branch = "release"
    };

    /// <summary>补入唯一的官方记录，不创建 clone 目录，不在打开管理窗口时下载内容。</summary>
    public async Task<ScriptRepositoryRegistration> EnsureOfficialRepositoryAsync(CancellationToken ct = default)
    {
        using var gate = await AcquireLockAsync("registry", ct).ConfigureAwait(false);
        var registrations = ReadRegistrations();
        var official = registrations.FirstOrDefault(r => r.IsOfficial);
        if (official is not null)
        {
            if (!official.IsEnabled || official.Kind != "official" || official.Branch != "release"
                || !string.Equals(official.Directory, OfficialDirectory, StringComparison.OrdinalIgnoreCase))
                throw new IOException("官方仓库配置无效，官方来源不能禁用、修改分支或重新定位。");
            return official;
        }
        official = CreateOfficialRegistration();
        registrations.Add(official);
        WriteJson(Path.Combine(RootDirectory, "repositories.json"), registrations);
        return official;
    }

    /// <summary>读取一个已注册来源，管理操作可以识别未下载和已经移除的记录。</summary>
    public Task<ScriptRepositoryRegistration> GetRepositoryAsync(string id, CancellationToken ct = default)
        => GetRegistrationAsync(id, ct);

    /// <summary>重新读取用户维护的本地仓库并发布当前版本；不会访问远端或修改任何任务引用。</summary>
    public async Task RefreshLocalRepositoryAsync(string id, CancellationToken ct = default)
    {
        var registration = await GetRegistrationAsync(id, ct).ConfigureAwait(false);
        if (!registration.IsEnabled || registration.IsRemote)
            throw new InvalidOperationException("只有已启用的本地仓库可以刷新目录。");
        using var sourceAccess = await EnterSourceAccessAsync(ct).ConfigureAwait(false);
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (Repository.IsValid(registration.Directory))
            {
                using var repository = new Repository(registration.Directory);
                if (repository.Head.Tip?.Tree["repo.json"]?.Target is not Blob)
                    throw new IOException("本地 Git 仓库的当前提交没有 repo.json 索引。");
            }
            else
            {
                _ = ReadFileSourceInventory(registration.Directory, ct);
            }
        }, ct).ConfigureAwait(false);
        NotifyUpdated(registration.Directory);
    }

    /// <summary>导入前校验 ZIP 中唯一的仓库根、repo 目录和 repo.json 索引协议，不写入任何仓库目录。</summary>
    public async Task ValidateRepositoryZipAsync(string zipFilePath, CancellationToken ct = default)
    {
        zipFilePath = Path.GetFullPath(zipFilePath);
        if (!File.Exists(zipFilePath))
            throw new FileNotFoundException("选择的 ZIP 文件不存在。", zipFilePath);
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var archive = ZipFile.OpenRead(zipFilePath);
            if (archive.Entries.Count == 0)
                throw new IOException("ZIP 压缩包为空，不是有效的脚本仓库。");
            var entries = archive.Entries.Select(entry => (Entry: entry, Path: ValidateZipEntryPath(entry))).ToArray();
            var indexes = entries.Where(item => item.Path.Equals("repo.json", StringComparison.OrdinalIgnoreCase)
                                                || item.Path.EndsWith("/repo.json", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (indexes.Length == 0)
                throw new IOException("ZIP 中没有找到 repo.json，不是有效的脚本仓库。");
            if (indexes.Length > 1)
                throw new IOException("ZIP 中存在多个 repo.json，无法确定唯一的仓库根目录。");

            var indexPath = indexes[0].Path;
            var rootPrefix = indexPath[..^"repo.json".Length];
            var repositoryPrefix = rootPrefix + "repo/";
            if (!entries.Any(item => item.Path.Equals(repositoryPrefix, StringComparison.OrdinalIgnoreCase)
                                     || item.Path.StartsWith(repositoryPrefix, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("repo.json 同级必须包含 repo 文件夹。");
            try
            {
                using var reader = new StreamReader(indexes[0].Entry.Open(), detectEncodingFromByteOrderMarks: true);
                if (JObject.Parse(reader.ReadToEnd())["indexes"] is not JArray)
                    throw new IOException("repo.json 缺少 indexes 资源清单。");
            }
            catch (JsonException ex)
            {
                throw new IOException("repo.json 不是有效的 JSON 文件。", ex);
            }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>拒绝 ZIP 路径穿越与符号链接；后续老导入器只能处理普通相对文件。</summary>
    private static string ValidateZipEntryPath(ZipArchiveEntry entry)
    {
        var path = entry.FullName.Replace('\\', '/');
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains('\0')
            || segments.Length == 0 || segments.Any(segment => segment is "." or ".." || segment.Contains(':'))
            || unixFileType == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
            throw new IOException($"ZIP 包含不安全的路径或链接：{entry.FullName}");
        return string.Join('/', segments) + (path.EndsWith('/') ? "/" : string.Empty);
    }

    /// <summary>新远程来源先保存配置，固定目录由仓库 ID 分配，不随名称或 URL 变化。</summary>
    public async Task<ScriptRepositoryRegistration> AddRemoteRepositoryAsync(string name, string url,
        string branch = "release", CancellationToken ct = default)
    {
        ValidateRemote(url, branch);
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("仓库名称不能为空。");
        using var gate = await AcquireLockAsync("registry", ct).ConfigureAwait(false);
        var registrations = ReadRegistrations();
        var id = Guid.NewGuid().ToString("N");
        var remoteId = Guid.NewGuid().ToString("N");
        var registration = new ScriptRepositoryRegistration
        {
            Id = id, Name = name.Trim(), Kind = "remote", Directory = GetManagedRemoteDirectory(id),
            Remotes = [new ScriptRepositoryRemote { Id = remoteId, Name = "默认", Url = url.Trim() }],
            ActiveRemoteId = remoteId, Branch = branch.Trim()
        };
        registrations.Add(registration);
        WriteJson(Path.Combine(RootDirectory, "repositories.json"), registrations);
        return registration;
    }

    /// <summary>底层保存完整远端集合，第三方界面仅修改当前项，不丢弃未展示的其他远端。</summary>
    public async Task<ScriptRepositoryRegistration> SaveRemoteRepositoryAsync(string id, string name,
        IReadOnlyList<ScriptRepositoryRemote> remotes, string activeRemoteId, string branch, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name) || remotes.Count == 0
            || remotes.Any(r => string.IsNullOrWhiteSpace(r.Id)) || remotes.Select(r => r.Id).Distinct().Count() != remotes.Count
            || !remotes.Any(r => r.Id == activeRemoteId))
            throw new ArgumentException("仓库名称或远端选择无效。");
        foreach (var remote in remotes) ValidateRemote(remote.Url, branch);
        using var gate = await AcquireLockAsync("registry", ct).ConfigureAwait(false);
        var registrations = ReadRegistrations();
        var index = registrations.FindIndex(r => r.Id == id);
        if (index < 0) throw new IOException("仓库不存在。");
        var existing = registrations[index];
        if (existing.IsOfficial) throw new InvalidOperationException("官方仓库渠道请使用共享的官方更新设置。");
        if (existing.Kind != "remote" || !existing.IsEnabled) throw new InvalidOperationException("该来源不能编辑远程配置。");
        var updated = new ScriptRepositoryRegistration
        {
            Id = existing.Id, Name = name.Trim(), Directory = existing.Directory, Kind = existing.Kind,
            IsEnabled = existing.IsEnabled, Remotes = remotes.ToArray(), ActiveRemoteId = activeRemoteId, Branch = branch.Trim()
        };
        registrations[index] = updated;
        WriteJson(Path.Combine(RootDirectory, "repositories.json"), registrations);
        return updated;
    }

    /// <summary>验证 Git 地址和分支，路径拼接前拒绝非法引用名称。</summary>
    public static void ValidateRemote(string url, string branch)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http" or "ssh"))
            throw new ArgumentException("请输入有效的 HTTPS、HTTP 或 SSH Git 地址。");
        if (string.IsNullOrWhiteSpace(branch) || !Reference.IsValidName("refs/heads/" + branch.Trim()))
            throw new ArgumentException("请输入有效的 Git 分支名称。");
    }
}
