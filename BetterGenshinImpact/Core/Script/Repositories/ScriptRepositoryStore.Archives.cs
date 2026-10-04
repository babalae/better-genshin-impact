using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Core.Script.Repositories;

/// <summary>按实际引用保留资源归档，旧计划使用的整仓 Git/ZIP 存储只做兼容读取。</summary>
public sealed partial class ScriptRepositoryStore
{
    /// <summary>读取固定版本的资源块映射；新存储不会保存整仓提交树。</summary>
    private Dictionary<string, string> ReadRetainedScopes(string repositoryId, string revision)
    {
        var path = Path.Combine(RootDirectory, "Snapshots", repositoryId, revision, "scopes.json");
        return File.Exists(path)
            ? JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path, Utf8))
              ?? throw new IOException("资源归档清单为空。") : new(StringComparer.Ordinal);
    }

    /// <summary>检查资源已经被完整保留，祖先目录归档可以覆盖后续的叶子引用。</summary>
    private bool HasRetainedResource(string repositoryId, string revision, string resourcePath)
    {
        var scopes = ReadRetainedScopes(repositoryId, revision);
        if (scopes.Keys.Any(root => root == resourcePath || resourcePath.StartsWith(root + "/", StringComparison.Ordinal))) return true;
        var directory = Path.Combine(RootDirectory, "Snapshots", repositoryId);
        if (File.Exists(Path.Combine(directory, revision + ".zip"))) return true;
        var objectPath = Path.Combine(directory, "objects.git");
        if (!Repository.IsValid(objectPath)) return false;
        using var legacy = new Repository(objectPath);
        return legacy.Refs["refs/pulonia/revisions/" + revision] is not null;
    }

    /// <summary>选定资源才保留内容；JS 的公共包和动态路线接口使用同版本共享归档。</summary>
    private async Task EnsureResourceRetainedAsync(ScriptResourceReference reference, string taskType, CancellationToken ct)
    {
        var sourcePath = "repo/" + reference.RelativePath;
        using var gate = await AcquireLockAsync("snapshot/" + reference.RepositoryId + "/" + reference.ApprovedRevision, ct)
            .ConfigureAwait(false);
        if (HasRetainedResource(reference.RepositoryId, reference.ApprovedRevision, sourcePath)) return;
        var registration = await GetRegistrationAsync(reference.RepositoryId, ct).ConfigureAwait(false);
        using var snapshot = await OpenSourceAsync(registration, reference.ApprovedRevision, ct).ConfigureAwait(false);
        await Task.Run(() =>
        {
            var scopes = ReadRetainedScopes(reference.RepositoryId, reference.ApprovedRevision);
            if (taskType == "javascript")
            {
                // 动态调用和目录查询无法预先列出路线依赖，因此按版本共享整个 pathing 子树。
                foreach (var common in new[] { "packages", "repo/pathing" })
                    if (snapshot.DirectoryExists(common) && !scopes.ContainsKey(common))
                        scopes[common] = WriteResourceArchive(snapshot, common, ct);
            }
            scopes[sourcePath] = WriteResourceArchive(snapshot, sourcePath, ct);
            ct.ThrowIfCancellationRequested();
            var directory = Path.Combine(RootDirectory, "Snapshots", reference.RepositoryId, reference.ApprovedRevision);
            Directory.CreateDirectory(directory);
            // 内容块先完整发布，最后原子替换映射；失败不暴露半份批准版本。
            WriteJson(Path.Combine(directory, "scopes.json"), scopes);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>把资源内容保存在受保护区域；相同子树按内容身份复用 ZIP，不逐个导入 Git 对象。</summary>
    private string WriteResourceArchive(ScriptRepositorySnapshot snapshot, string sourcePath, CancellationToken ct)
    {
        var identity = Hash(sourcePath + "\0" + snapshot.GetContentIdentity(sourcePath));
        var directory = Path.Combine(RootDirectory, "Snapshots", snapshot.Registration.Id, "Archives");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, identity + ".zip");
        if (File.Exists(destination)) return identity;
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
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
            // 不同版本可能同时保存同一公共块；一个完整发布者成功后直接共享它。
            try { File.Move(temporary, destination); }
            catch (IOException) when (File.Exists(destination)) { }
            return identity;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>预览仅缓存清单、设置和 README，不保留来源版本或提取完整项目。</summary>
    public async Task<string> PreparePreviewAsync(ScriptResourceReference reference, CancellationToken ct = default)
    {
        reference.Validate();
        var key = Hash(reference.RelativePath);
        using var gate = await AcquireLockAsync("preview/" + reference.RepositoryId + "/" + reference.ApprovedRevision + "/" + key, ct)
            .ConfigureAwait(false);
        using var snapshot = await OpenResourceReadAsync(reference, ct).ConfigureAwait(false);
        var root = "repo/" + reference.RelativePath;
        var directory = Path.Combine(RootDirectory, "Previews", reference.RepositoryId, reference.ApprovedRevision, key);
        return await Task.Run(() =>
        {
            var temporary = directory + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(temporary);
                WritePreview("manifest.json");
                var manifest = Project.Manifest.FromJson(File.ReadAllText(Path.Combine(temporary, "manifest.json")));
                if (!string.IsNullOrWhiteSpace(manifest.SettingsUi)) WritePreview(manifest.SettingsUi);
                if (snapshot.FileExists(root + "/README.md")) WritePreview("README.md");
                ct.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
                if (Directory.Exists(directory)) DeleteOwnedDirectory(directory);
                Directory.Move(temporary, directory);
                return directory;

                // 只写本次表单需要的文件，完整入口、模块和图片留到确认添加时提取。
                void WritePreview(string relative)
                {
                    ct.ThrowIfCancellationRequested();
                    var path = SafeCombine(temporary, relative);
                    var bytes = snapshot.ReadBytes(root + "/" + ScriptRepositorySnapshot.NormalizePath(relative));
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, bytes);
                }
            }
            finally { if (Directory.Exists(temporary)) DeleteOwnedDirectory(temporary); }
        }, ct).ConfigureAwait(false);
    }
}
