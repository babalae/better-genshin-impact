using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Helpers;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Core.Script;

/// <summary>按仓库身份更新固定目录，所有远端切换都保持同一个本地副本。</summary>
public partial class ScriptRepoUpdater
{
    /// <summary>官方旧入口与自动更新共用固定目录；调用者已经持有更新及来源锁。</summary>
    private async Task<(string, bool)> UpdateCenterRepoByGitCore(string repoUrl, CheckoutProgressHandler? onCheckoutProgress)
    {
        await ScriptRepositoryStore.Shared.EnsureOfficialRepositoryAsync().ConfigureAwait(false);
        return await UpdateRepositoryAtPathCoreAsync(repoUrl, CenterRepoPath, "release", onCheckoutProgress, default)
            .ConfigureAwait(false);
    }

    /// <summary>明确指定来源目录和分支的通用更新入口，复用进程内及跨实例更新互斥。</summary>
    public async Task<(string Directory, bool Updated)> UpdateRepositoryByGitAsync(string repoUrl, string directory,
        string branch, CheckoutProgressHandler? progress = null, CancellationToken ct = default)
    {
        ScriptRepositoryStore.ValidateRemote(repoUrl, branch);
        directory = ValidateManagedRepositoryDirectory(directory);
        await _repoWriteLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var sourceAccess = await ScriptRepositoryStore.Shared.EnterSourceAccessAsync(ct).ConfigureAwait(false);
            return await UpdateRepositoryAtPathCoreAsync(repoUrl.Trim(), directory, branch.Trim(), progress, ct).ConfigureAwait(false);
        }
        finally { _repoWriteLock.Release(); }
    }

    /// <summary>重置只清理托管 clone 目录，不移除注册和任何已经确认的资源缓存。</summary>
    public async Task ResetRepositoryAsync(string directory, CancellationToken ct = default)
    {
        directory = ValidateManagedRepositoryDirectory(directory);
        await _repoWriteLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var sourceAccess = await ScriptRepositoryStore.Shared.EnterSourceAccessAsync(ct).ConfigureAwait(false);
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                VerifySourceDirectory(directory);
                DirectoryHelper.DeleteReadOnlyDirectory(directory);
            }, ct).ConfigureAwait(false);
        }
        finally { _repoWriteLock.Release(); }
    }

    /// <summary>仅接受固定官方目录或按仓库 ID 分配的第三方目录，禁止操作本地开发来源。</summary>
    private static string ValidateManagedRepositoryDirectory(string directory)
    {
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var official = Path.GetFullPath(CenterRepoPath);
        var remoteRoot = Path.GetFullPath(Path.Combine(ReposPath, "Pulonia"));
        var name = Path.GetFileName(directory);
        if (!directory.Equals(official, StringComparison.OrdinalIgnoreCase)
            && !(string.Equals(Path.GetDirectoryName(directory), remoteRoot, StringComparison.OrdinalIgnoreCase)
                 && name.Length == 32 && name.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new IOException("只能更新或重置由仓库管理分配的固定 clone 目录。");
        VerifySourceDirectory(directory);
        return directory;
    }

    /// <summary>检查来源和其子项没有链接，删除或移动不能越过 Repos 边界。</summary>
    private static void VerifySourceDirectory(string directory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ReposPath));
        var absolute = Path.GetFullPath(directory);
        if (!absolute.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("仓库操作超出 Repos 目录。");
        for (var current = absolute; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((Directory.Exists(current) || File.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("仓库目录不能包含重解析点。");
            if (current.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
        }
        if (!Directory.Exists(absolute)) return;
        var pending = new System.Collections.Generic.Stack<string>();
        pending.Push(absolute);
        while (pending.TryPop(out var item))
            foreach (var entry in Directory.EnumerateFileSystemEntries(item))
            {
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("仓库操作不能遍历链接。");
                if (Directory.Exists(entry)) pending.Push(entry);
            }
    }

    /// <summary>在临时目录完成浅拉取与索引校验，完整发布后通知引用该来源的任务。</summary>
    private async Task<(string, bool)> UpdateRepositoryAtPathCoreAsync(string url, string directory, string branch,
        CheckoutProgressHandler? progress, CancellationToken ct)
    {
        ScriptRepositoryStore.ValidateRemote(url, branch);
        directory = ValidateManagedRepositoryDirectory(directory);
        var temporary = directory + ".update." + Guid.NewGuid().ToString("N");
        var backup = directory + ".backup." + Guid.NewGuid().ToString("N");
        var updated = await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            GlobalSettings.SetOwnerValidation(false);
            if (Repository.IsValid(directory))
            {
                using var repository = new Repository(directory);
                var remote = repository.Network.Remotes["origin"];
                if (remote?.Url == url && repository.Head.FriendlyName == branch && File.Exists(Path.Combine(directory, "repo.json")))
                {
                    // 版本和远端都一致时只查询分支 SHA，不重复下载同一内容。
                    var current = repository.Head.Tip?.Sha;
                    var next = repository.Network.ListReferences(url, CreateCredentialsHandler())
                        .FirstOrDefault(r => r.CanonicalName == "refs/heads/" + branch)?.TargetIdentifier;
                    ct.ThrowIfCancellationRequested();
                    if (next is null) throw new IOException("远端不存在分支：" + branch);
                    if (current == next) return false;
                }
            }
            try
            {
                // 拉取失败不触碰现有目录；镜像变化也不再按目录相似度分配新位置。
                Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
                CloneRepository(url, temporary, branch, progress, ct);
                ct.ThrowIfCancellationRequested();
                using (var repository = new Repository(temporary))
                {
                    var entry = repository.Head.Tip?.Tree["repo.json"];
                    if (entry?.Mode == Mode.SymbolicLink || entry?.Target is not Blob blob
                        || JObject.Parse(blob.GetContentText())["indexes"] is not JArray
                        || !File.Exists(Path.Combine(temporary, "repo.json")))
                        throw new IOException("拉取的仓库缺少有效的 repo.json 资源索引。");
                }
                var previousIndex = Path.Combine(directory, "repo_updated.json");
                var previous = File.Exists(previousIndex) ? File.ReadAllText(previousIndex)
                    : File.Exists(Path.Combine(directory, "repo.json")) ? File.ReadAllText(Path.Combine(directory, "repo.json")) : null;
                var nextIndex = File.ReadAllText(Path.Combine(temporary, "repo.json"));
                var marked = previous is not null && CalculateRepoOverlapRatio(previous, nextIndex) >= 0.5
                    ? AddUpdateMarkersToNewRepo(previous, nextIndex) : nextIndex;
                File.WriteAllText(Path.Combine(temporary, "repo_updated.json"), marked, new UTF8Encoding(false));
                ct.ThrowIfCancellationRequested();
                VerifySourceDirectory(directory);
                VerifySourceDirectory(temporary);
                if (Directory.Exists(directory)) Directory.Move(directory, backup);
                try { Directory.Move(temporary, directory); }
                catch
                {
                    if (Directory.Exists(backup)) Directory.Move(backup, directory);
                    throw;
                }
                // 替换成功之后清理备份；清理失败不能把已发布的新内容误报为拉取失败。
                TryDeleteUpdateDirectory(backup);
                return true;
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            finally { TryDeleteUpdateDirectory(temporary); }
        }, ct).ConfigureAwait(false);
        if (updated) ScriptRepositoryStore.Shared.NotifyUpdated(directory);
        return (directory, updated);
    }

    /// <summary>清理本次操作的临时或备份目录，保留失败诊断而不覆盖发布结果。</summary>
    private void TryDeleteUpdateDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return;
        try
        {
            VerifySourceDirectory(directory);
            DirectoryHelper.DeleteReadOnlyDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "清理仓库操作的临时目录失败: {Directory}", directory);
        }
    }
}
