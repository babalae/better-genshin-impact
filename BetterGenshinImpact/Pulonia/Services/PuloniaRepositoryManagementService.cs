using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Pulonia.Models;
using LibGit2Sharp.Handlers;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>协调 Pulonia 仓库配置与 Git 操作，资源选择和管理窗口使用相同来源身份。</summary>
public sealed class PuloniaRepositoryManagementService
{
    /// <summary>仓库注册与已经确认的资源存储。</summary>
    public ScriptRepositoryStore Repositories { get; }

    /// <summary>与现有官方弹窗共用的渠道配置，避免保存第二份官方选择。</summary>
    public ScriptConfig OfficialConfig => TaskContext.Instance().Config.ScriptConfig;

    /// <summary>复用调用方的存储，所有 Git 操作仍经过统一更新器的互斥。</summary>
    public PuloniaRepositoryManagementService(ScriptRepositoryStore repositories) => Repositories = repositories;

    /// <summary>获取当前远端，官方自定义地址与旧官方更新窗口同步。</summary>
    public string GetRemoteUrl(ScriptRepositoryRegistration repository)
    {
        if (repository.IsOfficial)
        {
            var channel = OfficialConfig.SelectedChannelName;
            if (channel == "自定义") return OfficialConfig.CustomRepoUrl;
            return ScriptRepoUpdater.RepoChannels.GetValueOrDefault(channel ?? string.Empty)
                   ?? ScriptRepoUpdater.RepoChannels["CNB"];
        }
        return (repository.Remotes.FirstOrDefault(r => r.Id == repository.ActiveRemoteId)
                ?? repository.Remotes.FirstOrDefault())?.Url ?? string.Empty;
    }

    /// <summary>编辑第三方当前地址，底层已经配置的其他远端原样保留。</summary>
    public async Task<ScriptRepositoryRegistration> SaveRemoteAsync(string id, string name, string url, string branch,
        CancellationToken ct = default)
    {
        var current = await Repositories.GetRepositoryAsync(id, ct).ConfigureAwait(false);
        if (current.IsOfficial) throw new InvalidOperationException("官方渠道使用共享的官方设置。");
        var active = current.Remotes.FirstOrDefault(r => r.Id == current.ActiveRemoteId) ?? current.Remotes.FirstOrDefault()
                     ?? throw new IOException("仓库没有配置远端。");
        var remotes = current.Remotes.Select(r => r.Id == active.Id
            ? new ScriptRepositoryRemote { Id = r.Id, Name = r.Name, Url = url.Trim() } : r).ToArray();
        return await Repositories.SaveRemoteRepositoryAsync(id, name, remotes, active.Id, branch, ct).ConfigureAwait(false);
    }

    /// <summary>按已经保存的远端和分支更新固定目录，本地引用不能被程序拉取覆盖。</summary>
    public async Task<bool> UpdateAsync(string id, CheckoutProgressHandler? progress, CancellationToken ct = default)
    {
        var repository = await Repositories.GetRepositoryAsync(id, ct).ConfigureAwait(false);
        ValidateManagedSource(repository);
        var url = GetRemoteUrl(repository);
        if (url == "https://example.com/custom-repo") throw new ArgumentException("请填写有效的自定义镜像地址。");
        var (_, updated) = await ScriptRepoUpdater.Instance.UpdateRepositoryByGitAsync(url, repository.Directory,
            repository.IsOfficial ? "release" : repository.Branch, progress, ct).ConfigureAwait(false);
        if (repository.IsOfficial) OfficialConfig.LastUpdateScriptRepoTime = DateTime.Now;
        return updated;
    }

    /// <summary>重置仅适用于托管远程来源，不移除官方记录和固定资源版本。</summary>
    public async Task ResetAsync(string id, CancellationToken ct = default)
    {
        var repository = await Repositories.GetRepositoryAsync(id, ct).ConfigureAwait(false);
        ValidateManagedSource(repository);
        await ScriptRepoUpdater.Instance.ResetRepositoryAsync(repository.Directory, ct).ConfigureAwait(false);
    }

    /// <summary>刷新用户维护的本地仓库目录，不访问远端也不修改任务使用版本。</summary>
    public Task RefreshLocalAsync(string id, CancellationToken ct = default)
        => Repositories.RefreshLocalRepositoryAsync(id, ct);

    /// <summary>识别单个本地来源，拒绝快捷方式、链接、普通文件或不存在的路径。</summary>
    public static PuloniaLocalRepositorySourceKind ClassifyLocalSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("请选择一个仓库目录或 ZIP 压缩包。");
        path = Path.GetFullPath(path);
        if (!Directory.Exists(path) && !File.Exists(path))
            throw new IOException("选择的目录或 ZIP 文件不存在。");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("请选择实际的仓库目录或 ZIP 文件，不支持链接。");
        if (Directory.Exists(path)) return PuloniaLocalRepositorySourceKind.Directory;
        if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
            return PuloniaLocalRepositorySourceKind.Zip;
        throw new IOException("只支持仓库目录或 ZIP 压缩包，不支持普通文件和快捷方式。");
    }

    /// <summary>统一处理选择器和拖放来源，先识别类型，再沿用各自的索引校验与注册流程。</summary>
    public Task<ScriptRepositoryRegistration> AddLocalSourceAsync(string path,
        Action<int, string>? progress = null, CancellationToken ct = default)
    {
        var kind = ClassifyLocalSource(path);
        path = Path.GetFullPath(path);
        if (kind == PuloniaLocalRepositorySourceKind.Zip) return ImportLocalZipAsync(path, progress, ct);
        progress?.Invoke(0, "正在校验目录中的 repo.json…");
        return Repositories.AddRepositoryAsync(path, ct);
    }

    /// <summary>先校验离线包，再复用老配置组导入器解压到共享 Repos 目录并注册给 Pulonia。</summary>
    public async Task<ScriptRepositoryRegistration> ImportLocalZipAsync(string zipFilePath,
        Action<int, string>? progress = null, CancellationToken ct = default)
    {
        progress?.Invoke(0, "正在校验 ZIP 中的 repo.json…");
        await Repositories.ValidateRepositoryZipAsync(zipFilePath, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var directory = await ScriptRepoUpdater.Instance.ImportLocalRepoZip(zipFilePath, progress).ConfigureAwait(false);
        // 老导入器开始写入后即完成注册，避免关闭窗口留下只有磁盘内容、没有 Pulonia 身份的半成品。
        var registration = await Repositories.AddRepositoryAsync(directory, CancellationToken.None).ConfigureAwait(false);
        Repositories.NotifyUpdated(directory);
        return registration;
    }

    /// <summary>防止使用本地或已经移除的来源执行托管目录操作。</summary>
    private void ValidateManagedSource(ScriptRepositoryRegistration repository)
    {
        if (!repository.IsEnabled || !repository.IsRemote)
            throw new InvalidOperationException("本地引用或已移除仓库不能拉取或重置。");
        var expectedDirectory = repository.IsOfficial ? Repositories.OfficialDirectory : Repositories.GetManagedRemoteDirectory(repository.Id);
        if (!string.Equals(Path.GetFullPath(repository.Directory), Path.GetFullPath(expectedDirectory), StringComparison.OrdinalIgnoreCase))
            throw new IOException("仓库 clone 目录与固定身份不匹配，不能更新或重置。");
    }
}
