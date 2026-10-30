using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Settings;
using BetterGenshinImpact.Helpers;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Core.Script.Repositories;

/// <summary>
/// 按实际引用保留资源归档。每个仓库一份 <c>Snapshots\&lt;仓库ID&gt;\retained.json</c> 索引，
/// 归档块文件为 <c>Snapshots\&lt;仓库ID&gt;\blk_xxxx.zip</c>；来源修订和内容身份只记录在索引中。
/// </summary>
public sealed partial class ScriptRepositoryStore
{
    /// <summary>仓库的归档目录：<c>Snapshots\&lt;仓库ID&gt;</c>。</summary>
    private string GetSnapshotDirectory(string repositoryId) => Path.Combine(RootDirectory, "Snapshots", repositoryId);

    /// <summary>读取仓库的已保留归档索引；尚未保留任何资源时返回空索引，损坏的索引不能当作空索引覆盖。</summary>
    /// <param name="snapshotDirectory">仓库归档目录</param>
    internal static ScriptRetainedIndex ReadRetainedIndex(string snapshotDirectory)
    {
        var path = Path.Combine(snapshotDirectory, "retained.json");
        if (!File.Exists(path)) return new ScriptRetainedIndex();
        // 读取不取 retain 锁；写入方以原子替换发布新索引，读取期间替换也只会读到完整的旧版本。
        var index = JsonConvert.DeserializeObject<ScriptRetainedIndex>(ReadText(path))
                    ?? throw new IOException("资源归档索引为空。");
        if (index.Blocks is null || index.Revisions is null || index.Revisions.Values.Any(scopes => scopes is null))
            throw new IOException("资源归档索引格式无效。");
        return index;
    }

    /// <summary>检查索引中该修订是否已保留资源，祖先目录范围可以覆盖后续的叶子引用。</summary>
    private static bool HasRetainedResource(ScriptRetainedIndex index, string revision, string resourcePath)
        => index.Revisions.TryGetValue(revision, out var scopes)
           && scopes.Keys.Any(root => root == resourcePath || resourcePath.StartsWith(root + "/", StringComparison.Ordinal));

    /// <summary>检查指定修订是否已保留该资源，供预览决定读取保留归档还是当前来源。</summary>
    private bool HasRetainedResource(string repositoryId, string revision, string resourcePath)
        => HasRetainedResource(ReadRetainedIndex(GetSnapshotDirectory(repositoryId)), revision, resourcePath);

    /// <summary>
    /// 选定资源才保留内容；首次保留前校验内容，校验失败不产生任何归档。
    /// JS 同时保留同版本的公共包和 pathing 子树，动态依赖和路线接口只能读取这一份固定来源。
    /// </summary>
    private async Task EnsureResourceRetainedAsync(ScriptResourceReference reference, string taskType, CancellationToken ct)
    {
        var directory = GetSnapshotDirectory(reference.RepositoryId);
        using var gate = await AcquireLockAsync("retain." + reference.RepositoryId, ct).ConfigureAwait(false);
        if (HasRetainedResource(ReadRetainedIndex(directory), reference.ApprovedRevision, "repo/" + reference.RelativePath)) return;
        var registration = GetRegistration(reference.RepositoryId);
        using var snapshot = await OpenSourceAsync(registration, reference.ApprovedRevision, ct).ConfigureAwait(false);
        await Task.Run(() => RetainFromSnapshot(directory, snapshot, reference, taskType, ct), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 固定来源当前版本，在同一读取会话中先由调用方核对内容，再保留该资源，返回指向该版本的新引用。
    /// 核对与保留读取同一份固定内容，不需要先释放来源锁再重新打开；确认期间来源被更新时不会保留未审阅的版本。
    /// 锁顺序与首次保留一致：retain → sources。
    /// </summary>
    /// <param name="reference">任务当前引用；只使用其中的仓库与相对路径</param>
    /// <param name="taskType">资源能力类型，决定保留范围和内容校验</param>
    /// <param name="verify">在固定快照上核对修订与内容指纹，不符合时抛出异常并放弃保留</param>
    /// <param name="ct">取消令牌</param>
    public async Task<ScriptResourceReference> RetainCurrentAsync(ScriptResourceReference reference, string taskType,
        Action<ScriptRepositorySnapshot> verify, CancellationToken ct = default)
    {
        reference.Validate();
        var directory = GetSnapshotDirectory(reference.RepositoryId);
        using var gate = await AcquireLockAsync("retain." + reference.RepositoryId, ct).ConfigureAwait(false);
        var registration = GetRegistration(reference.RepositoryId);
        using var snapshot = await OpenSourceAsync(registration, null, ct).ConfigureAwait(false);
        var updated = reference.WithRevision(snapshot.Revision);
        await Task.Run(() =>
        {
            verify(snapshot);
            RetainFromSnapshot(directory, snapshot, updated, taskType, ct);
        }, ct).ConfigureAwait(false);
        return updated;
    }

    /// <summary>
    /// 从已固定的来源会话保留一个资源范围；该修订已覆盖此资源时不重复写入。调用方持有 retain 锁。
    /// </summary>
    /// <param name="directory">仓库归档目录</param>
    /// <param name="snapshot">与引用修订一致的来源读取会话</param>
    /// <param name="reference">要保留的资源引用</param>
    /// <param name="taskType">资源能力类型</param>
    /// <param name="ct">取消令牌</param>
    private void RetainFromSnapshot(string directory, ScriptRepositorySnapshot snapshot, ScriptResourceReference reference,
        string taskType, CancellationToken ct)
    {
        var sourcePath = "repo/" + reference.RelativePath;
        var index = ReadRetainedIndex(directory);
        if (HasRetainedResource(index, reference.ApprovedRevision, sourcePath)) return;
        // 资源已被同修订的其他范围覆盖时不会走到这里，例如 JS 共享的 repo/pathing 已包含该路线；
        // 此时路线正文由执行器解析时校验。
        ValidateResourceContent(snapshot, sourcePath, taskType, ct);
        if (!index.Revisions.TryGetValue(reference.ApprovedRevision, out var scopes))
            index.Revisions[reference.ApprovedRevision] = scopes = new Dictionary<string, string>(StringComparer.Ordinal);
        Directory.CreateDirectory(directory);
        if (taskType == "javascript")
        {
            // 动态调用和目录查询无法预先列出路线依赖，因此按版本共享整个 pathing 子树。
            foreach (var common in new[] { "packages", "repo/pathing" })
                if (snapshot.DirectoryExists(common) && !scopes.ContainsKey(common))
                    scopes[common] = WriteResourceArchive(index, directory, snapshot, common, ct);
        }
        scopes[sourcePath] = WriteResourceArchive(index, directory, snapshot, sourcePath, ct);
        ct.ThrowIfCancellationRequested();
        // 归档块先完整发布，最后原子替换索引；失败不暴露半份批准版本。
        // 块已写入、索引尚未更新时崩溃会留下孤儿块，与“不自动清理保留版本”一致，暂不清理。
        WriteJson(Path.Combine(directory, "retained.json"), index);
    }

    /// <summary>
    /// 首次保留前校验资源：路线和键鼠校验 JSON 语法，JS 校验清单、入口和声明的设置文件。
    /// 校验失败时抛出异常，调用方不会产生任务引用。
    /// </summary>
    private void ValidateResourceContent(ScriptRepositorySnapshot snapshot, string sourcePath, string taskType,
        CancellationToken ct)
    {
        var isDirectory = snapshot.DirectoryExists(sourcePath);
        if (!isDirectory && !snapshot.FileExists(sourcePath))
            throw new FileNotFoundException("来源版本中不存在资源：" + sourcePath["repo/".Length..]);
        if (taskType == "javascript")
        {
            if (!isDirectory) throw new IOException("JS 项目校验失败：所选资源不是目录。");
            // Manifest 校验需要真实目录，解压到资源区根下的短临时目录，校验后删除。
            var temporary = Path.Combine(RootDirectory, ShortId.NewTemporaryName());
            try
            {
                if (ExtractFiles(snapshot, sourcePath, temporary, false, ct) == 0)
                    throw new IOException("资源目录为空，不能确认空项目。");
                ValidateJavaScriptProject(temporary);
            }
            finally { TryDeleteOwnedDirectory(temporary); }
            return;
        }
        // 只校验路线正文的 JSON 语法，具体任务协议继续由各能力执行器解释。
        var files = isDirectory ? snapshot.EnumerateFiles(sourcePath).Select(entry => entry.Path).ToArray() : [sourcePath];
        if (files.Length == 0) throw new IOException("资源目录为空，不能确认空资源。");
        foreach (var file in files.Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            ct.ThrowIfCancellationRequested();
            Newtonsoft.Json.Linq.JToken.Parse(snapshot.ReadText(file));
        }
    }

    /// <summary>
    /// 把资源范围写成归档块并登记到索引；相同内容身份的范围跨修订复用已有块。
    /// 调用方持有 retain 锁；块文件先写同级临时文件，完整后再改名为 <c>blk_xxxx.zip</c>。
    /// </summary>
    private static string WriteResourceArchive(ScriptRetainedIndex index, string directory,
        ScriptRepositorySnapshot snapshot, string sourcePath, CancellationToken ct)
    {
        var identity = Hash(sourcePath + "\0" + snapshot.GetContentIdentity(sourcePath));
        foreach (var block in index.Blocks)
            if (block.Value == identity && File.Exists(Path.Combine(directory, block.Key + ".zip")))
                return block.Key;
        string blockId;
        do blockId = ShortId.Create("blk");
        while (index.Blocks.ContainsKey(blockId) || File.Exists(Path.Combine(directory, blockId + ".zip")));
        var destination = Path.Combine(directory, blockId + ".zip");
        var temporary = Path.Combine(directory, ShortId.NewTemporaryName());
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                var isDirectory = snapshot.DirectoryExists(sourcePath);
                if (isDirectory) archive.CreateEntry(sourcePath + "/");
                var files = isDirectory ? snapshot.EnumerateFiles(sourcePath)
                    : [new ScriptRepositoryEntry(sourcePath, 0, snapshot.LastWriteTime)];
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    var entry = archive.CreateEntry(file.Path, CompressionLevel.Fastest);
                    var date = file.LastWriteTime;
                    entry.LastWriteTime = date.Year is >= 1980 and <= 2107 ? new DateTimeOffset(date) : new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using var input = snapshot.OpenRead(file.Path);
                    using var output = entry.Open();
                    input.CopyTo(output);
                }
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination);
            index.Blocks[blockId] = identity;
            return blockId;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>
    /// 预览仅提取清单、设置和 README 到一次性目录 <c>Previews\pv_xxxxxxxx</c>，不保留来源版本或提取完整项目。
    /// 打开 HTML 设置页时再提取整个设置页目录，并检查文件数与大小上限。
    /// 调用方持有返回的租约，直到界面不再读取 README 或设置页；进程崩溃残留的预览目录暂不清理。
    /// </summary>
    public async Task<ScriptPreviewLease> PreparePreviewAsync(ScriptResourceReference reference, CancellationToken ct = default,
        bool includeSettingsPage = false)
    {
        reference.Validate();
        using var snapshot = await OpenResourceReadAsync(reference, ct).ConfigureAwait(false);
        var root = "repo/" + reference.RelativePath;
        // 每次预览使用独立目录，切换选择时旧租约单独释放，不需要预览锁。
        var directory = Path.Combine(RootDirectory, "Previews", ShortId.Create("pv", 8));
        return await Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(directory);
                WritePreview("manifest.json");
                var manifest = Project.Manifest.FromJson(File.ReadAllText(Path.Combine(directory, "manifest.json")));
                if (!string.IsNullOrWhiteSpace(manifest.SettingsUi)) WritePreview(manifest.SettingsUi);
                if (snapshot.FileExists(root + "/README.md")) WritePreview("README.md");
                if (includeSettingsPage && !string.IsNullOrWhiteSpace(manifest.SettingsHtml))
                {
                    var entry = ScriptSettingsHtmlPage.NormalizeEntry(manifest.SettingsHtml);
                    var slash = entry.LastIndexOf('/');
                    var files = snapshot.EnumerateFiles(slash < 0 ? root : root + "/" + entry[..slash]);
                    ScriptSettingsHtmlPage.CheckLimits(files.Select(file => file.Length).ToArray());
                    foreach (var file in files) WritePreview(file.Path[(root.Length + 1)..]);
                }
                ct.ThrowIfCancellationRequested();
                return new ScriptPreviewLease(directory, () => TryDeleteOwnedDirectory(directory));
            }
            catch
            {
                TryDeleteOwnedDirectory(directory);
                throw;
            }

            // 只写本次表单需要的文件，完整入口、模块和图片留到确认添加时保留。
            void WritePreview(string relative)
            {
                ct.ThrowIfCancellationRequested();
                var path = SafeCombine(directory, relative);
                var bytes = snapshot.ReadBytes(root + "/" + ScriptRepositorySnapshot.NormalizePath(relative));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
        }, ct).ConfigureAwait(false);
    }
}
