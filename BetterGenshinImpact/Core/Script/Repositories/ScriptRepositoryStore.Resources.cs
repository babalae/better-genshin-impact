using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Project;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Core.Script.Repositories;

/// <summary>按来源版本提取资源并准备隔离的可写目录。</summary>
public sealed partial class ScriptRepositoryStore
{
    /// <summary>为仓库文件计算与现有 Pulonia 一致的版本；JS 严格只检查两个入口文件。</summary>
    public static string ComputeVersion(ScriptRepositorySnapshot snapshot, string relativePath, string taskType,
        bool recursive = true, bool directory = false)
    {
        relativePath = ScriptRepositorySnapshot.NormalizePath(relativePath);
        var root = "repo/" + relativePath;
        if (taskType == "javascript")
            return Hash("main.js\0" + Hash(snapshot.ReadBytes(root + "/main.js")) + "\n"
                        + "manifest.json\0" + Hash(snapshot.ReadBytes(root + "/manifest.json")) + "\n");
        if (!directory) return Hash(snapshot.ReadBytes(root));
        var manifest = new StringBuilder();
        var files = snapshot.EnumerateFiles(root).Where(e => e.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Path[(root.Length + 1)..]).Where(p => recursive || !p.Contains('/'))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0 || files.Length > 10000) throw new IOException("目录中没有资源或资源数量超过安全上限。");
        foreach (var file in files) manifest.Append(file).Append('\0').Append(Hash(snapshot.ReadBytes(root + "/" + file))).Append('\n');
        return Hash(manifest.ToString());
    }

    /// <summary>提取固定版本的资源，缓存缺失时只从相同版本恢复。</summary>
    public async Task<string> MaterializeAsync(ScriptResourceReference reference, string taskType,
        CancellationToken ct = default)
    {
        reference.Validate();
        var key = Hash(reference.RelativePath);
        var directory = Path.Combine(RootDirectory, "Cache", reference.RepositoryId, reference.ApprovedRevision, key);
        using var gate = await AcquireLockAsync("cache/" + reference.RepositoryId + "/" + reference.ApprovedRevision + "/" + key, ct)
            .ConfigureAwait(false);
        return await Task.Run(async () =>
        {
            await EnsureResourceRetainedAsync(reference, taskType, ct).ConfigureAwait(false);
            using var snapshot = await OpenApprovedAsync(reference, ct).ConfigureAwait(false);
            var source = "repo/" + reference.RelativePath;
            var isDirectory = snapshot.DirectoryExists(source);
            var result = isDirectory ? Path.Combine(directory, "content")
                : Path.Combine(directory, "content", Path.GetFileName(reference.RelativePath));
            if (ValidateCache(directory, ct)) return result;
            if (!isDirectory && !snapshot.FileExists(source)) throw new FileNotFoundException("来源版本中不存在资源：" + reference.RelativePath);
            var temporary = directory + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.Combine(temporary, "content"));
                var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
                if (isDirectory)
                {
                    foreach (var entry in snapshot.EnumerateFiles(source))
                        WriteEntry(entry.Path, entry.Path[(source.Length + 1)..]);
                    // 保留同一提交的公共包。动态导入也只能使用这一份固定来源，不能读取更新后的包。
                    if (taskType == "javascript" && snapshot.DirectoryExists("packages"))
                        foreach (var entry in snapshot.EnumerateFiles("packages")) WriteEntry(entry.Path, entry.Path);
                }
                else WriteEntry(source, Path.GetFileName(reference.RelativePath));
                if (hashes.Count == 0) throw new IOException("资源目录为空，不能发布空缓存。");
                if (taskType == "javascript")
                {
                    var content = Path.Combine(temporary, "content");
                    try
                    {
                        var manifest = Manifest.FromJson(File.ReadAllText(Path.Combine(content, "manifest.json")));
                        SafeCombine(content, manifest.Main);
                        manifest.Validate(content);
                        if (manifest.Library is null || manifest.SavedFiles is null)
                            throw new IOException("library 和 saved_files 不能为 null。");
                        // 设置定义与完整代码一起校验，不能确认一份无法打开设置表单的项目。
                        if (!string.IsNullOrWhiteSpace(manifest.SettingsUi)
                            && !File.Exists(SafeCombine(content, manifest.SettingsUi)))
                            throw new FileNotFoundException("manifest 指定的设置文件不存在。");
                        manifest.LoadSettingItems(content);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    { throw new IOException("JS 项目校验失败：" + ex.Message, ex); }
                }
                else
                {
                    // 只校验路线正文的 JSON 语法，具体任务协议继续由各能力执行器解释。
                    foreach (var file in hashes.Keys.Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                        Newtonsoft.Json.Linq.JToken.Parse(File.ReadAllText(SafeCombine(Path.Combine(temporary, "content"), file)));
                }
                WriteJson(Path.Combine(temporary, "manifest.json"), hashes);
                ct.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
                if (Directory.Exists(directory)) DeleteOwnedDirectory(directory);
                Directory.Move(temporary, directory);
                return result;

                // 内容文件和完整性清单一次性发布；异常或取消不得暴露半份项目。
                void WriteEntry(string sourcePath, string targetPath)
                {
                    ct.ThrowIfCancellationRequested();
                    var bytes = snapshot.ReadBytes(sourcePath);
                    var path = SafeCombine(Path.Combine(temporary, "content"), targetPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, bytes);
                    hashes[ScriptRepositorySnapshot.NormalizePath(targetPath)] = Hash(bytes);
                }
            }
            finally { if (Directory.Exists(temporary)) DeleteOwnedDirectory(temporary); }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>校验缓存所有发布文件，不能仅靠两个版本文件认定完整项目没有损坏。</summary>
    private static bool ValidateCache(string directory, CancellationToken ct)
    {
        var manifest = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifest)) return false;
        try
        {
            var entries = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(manifest, Utf8));
            if (entries is null || entries.Count == 0) return false;
            if (EnumerateSourceFiles(Path.Combine(directory, "content"), ct).Length != entries.Count) return false;
            foreach (var item in entries)
            {
                ct.ThrowIfCancellationRequested();
                var path = SafeCombine(Path.Combine(directory, "content"), item.Key);
                if (!File.Exists(path) || Hash(File.ReadAllBytes(path)) != item.Value) return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException) { return false; }
    }

    /// <summary>在受控根目录内定位文件，检查每级已存在路径，禁止重解析点。</summary>
    internal static string SafeCombine(string root, string relative)
    {
        relative = ScriptRepositorySnapshot.NormalizePath(relative);
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithin(root, path)) throw new IOException("资源路径逃逸到根目录外。");
        for (var current = path; current is not null && IsWithin(root, current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("资源路径不能包含重解析点。");
        return path;
    }

    /// <summary>只删除资源区中经过完整边界和链接校验的程序临时目录。</summary>
    private void DeleteOwnedDirectory(string path)
    {
        path = Path.GetFullPath(path);
        if (!IsWithin(RootDirectory, path) || path.Equals(RootDirectory, StringComparison.OrdinalIgnoreCase))
            throw new IOException("拒绝删除资源区以外的目录。");
        EnumerateSourceFiles(path, CancellationToken.None);
        Directory.Delete(path, true);
    }

    /// <summary>取得按仓库和脚本共享的工作目录，直到执行退出都保持跨进程独占。</summary>
    public async Task<ScriptWorkspaceLease> AcquireWorkspaceAsync(ScriptResourceReference reference, CancellationToken ct = default)
    {
        var cachePath = await MaterializeAsync(reference, "javascript", ct).ConfigureAwait(false);
        var key = Hash(reference.RelativePath);
        var gate = await AcquireLockAsync("workspace/" + reference.RepositoryId + "/" + key, ct).ConfigureAwait(false);
        try
        {
            var directory = Path.Combine(RootDirectory, "Workspaces", reference.RepositoryId, key);
            await Task.Run(() => PrepareWorkspace(directory, cachePath, ct), ct).ConfigureAwait(false);
            return new ScriptWorkspaceLease(directory, gate);
        }
        catch { gate.Dispose(); throw; }
    }

    /// <summary>准备可写副本并恢复声明的用户数据；代码缓存始终保持不可变。</summary>
    private void PrepareWorkspace(string directory, string cachePath, CancellationToken ct)
    {
        var temporary = directory + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var backup = directory + "." + Guid.NewGuid().ToString("N") + ".old";
        try
        {
            Directory.CreateDirectory(temporary);
            foreach (var source in EnumerateSourceFiles(cachePath, ct))
            {
                var target = SafeCombine(temporary, Path.GetRelativePath(cachePath, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target);
            }
            var manifest = Manifest.FromJson(File.ReadAllText(Path.Combine(cachePath, "manifest.json")));
            if (Directory.Exists(directory))
            {
                // 按旧、新清单的并集保存进度，版本切换时不丢失旧版本声明的数据。
                var previous = Manifest.FromJson(File.ReadAllText(Path.Combine(directory, "manifest.json")));
                var patterns = previous.SavedFiles.Concat(manifest.SavedFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                foreach (var source in EnumerateSourceFiles(directory, ct))
                {
                    var relative = Path.GetRelativePath(directory, source).Replace('\\', '/');
                    if (relative is "manifest.json" or "main.js" || !patterns.Any(pattern => MatchesSavedFile(pattern, relative))) continue;
                    var target = SafeCombine(temporary, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(source, target, true);
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(directory)) Directory.Move(directory, backup);
            try { Directory.Move(temporary, directory); }
            catch { if (Directory.Exists(backup)) Directory.Move(backup, directory); throw; }
            if (Directory.Exists(backup)) DeleteOwnedDirectory(backup);
        }
        finally { if (Directory.Exists(temporary)) DeleteOwnedDirectory(temporary); }
    }

    /// <summary>沿用 saved_files 的目录与通配符语义，禁止保存路径跳出项目。</summary>
    private static bool MatchesSavedFile(string pattern, string relative)
    {
        pattern = pattern.Replace('\\', '/');
        while (pattern.StartsWith("./", StringComparison.Ordinal)) pattern = pattern[2..];
        if (pattern.StartsWith('/') || pattern.Contains(':') || pattern.Split('/').Any(p => p == ".."))
            throw new IOException("saved_files 包含不安全的路径。");
        return relative.StartsWith(pattern.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)
               || FileSystemName.MatchesSimpleExpression(pattern, relative, true);
    }
}
