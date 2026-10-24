using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.Core.Script.Repositories;

/// <summary>
/// 计算固定版本的资源指纹、确认保留资源并准备隔离的 JS 可写目录。
/// 路线和键鼠直接从已保留归档读取正文，不生成提取缓存。
/// </summary>
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
        var files = ListDirectoryResources(snapshot, relativePath, recursive);
        if (files.Count == 0 || files.Count > 10000) throw new IOException("目录中没有资源或资源数量超过安全上限。");
        var manifest = new StringBuilder();
        foreach (var file in files) manifest.Append(file).Append('\0').Append(Hash(snapshot.ReadBytes(root + "/" + file))).Append('\n');
        return Hash(manifest.ToString());
    }

    /// <summary>
    /// 列出仓库目录引用中的 JSON 资源，返回相对该目录、使用正斜杠的路径，按忽略大小写的序号排序。
    /// 目录指纹、运行准备和创建任务共用这一份清单与顺序，保证同一目录得到同一指纹。
    /// </summary>
    /// <param name="snapshot">固定版本的读取会话</param>
    /// <param name="relativePath">相对仓库 repo/ 内容目录的目录路径</param>
    /// <param name="recursive">是否包含子目录</param>
    public static IReadOnlyList<string> ListDirectoryResources(ScriptRepositorySnapshot snapshot, string relativePath,
        bool recursive)
    {
        var root = "repo/" + ScriptRepositorySnapshot.NormalizePath(relativePath);
        return snapshot.EnumerateFiles(root).Where(e => e.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Path[(root.Length + 1)..]).Where(p => recursive || !p.Contains('/'))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// 确保资源已保留到归档；首次保留时校验内容，校验失败不产生任务引用。
    /// 之后路线和键鼠从 <see cref="OpenApprovedAsync"/> 读取正文，JS 由 <see cref="AcquireWorkspaceAsync"/> 准备目录。
    /// </summary>
    public async Task RetainAsync(ScriptResourceReference reference, string taskType, CancellationToken ct = default)
    {
        reference.Validate();
        await EnsureResourceRetainedAsync(reference, taskType, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 把固定快照中某个范围的文件写入目标目录，返回写入的文件数。
    /// keepPrefix 为 false 时去掉范围前缀，为 true 时保留完整的仓库相对路径。
    /// </summary>
    private static int ExtractFiles(ScriptRepositorySnapshot snapshot, string sourceRoot, string targetRoot,
        bool keepPrefix, CancellationToken ct)
    {
        var count = 0;
        foreach (var entry in snapshot.EnumerateFiles(sourceRoot))
        {
            ct.ThrowIfCancellationRequested();
            var path = SafeCombine(targetRoot, keepPrefix ? entry.Path : entry.Path[(sourceRoot.Length + 1)..]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var input = snapshot.OpenRead(entry.Path))
            using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                input.CopyTo(output);
            count++;
        }
        return count;
    }

    /// <summary>校验已解压的 JS 项目：清单、入口、library 与 saved_files，以及声明的设置文件。</summary>
    /// <param name="content">项目根目录</param>
    /// <returns>解析后的项目清单</returns>
    private static Manifest ValidateJavaScriptProject(string content)
    {
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
            return manifest;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { throw new IOException("JS 项目校验失败：" + ex.Message, ex); }
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

    /// <summary>尽力删除资源区内的临时目录；目录不存在或删除失败时保留残留，不覆盖原始异常。</summary>
    private void TryDeleteOwnedDirectory(string path)
    {
        try { if (Directory.Exists(path)) DeleteOwnedDirectory(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// JS 工作目录镜像仓库相对路径：<c>Workspaces\&lt;仓库ID&gt;\&lt;资源相对路径&gt;</c>，
    /// 用户可以直接找到脚本的 saved_files 数据。
    /// 暂不支持：仓库中嵌套的 JS 项目，或只差大小写的两个项目路径，它们会落到同一目录。
    /// </summary>
    private string GetWorkspaceDirectory(ScriptResourceReference reference)
        => SafeCombine(Path.Combine(RootDirectory, "Workspaces", reference.RepositoryId), reference.RelativePath);

    /// <summary>
    /// 取得按仓库和脚本共享的工作目录，直到执行退出都保持跨进程独占。
    /// 工作目录锁是同级的 <c>&lt;目录名&gt;.lock</c>，替换目录时不受影响，用完保留供下次复用。
    /// </summary>
    public async Task<ScriptWorkspaceLease> AcquireWorkspaceAsync(ScriptResourceReference reference, CancellationToken ct = default)
    {
        await RetainAsync(reference, "javascript", ct).ConfigureAwait(false);
        var directory = GetWorkspaceDirectory(reference);
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        var gate = await AcquireFileLockAsync(directory + ".lock", ct).ConfigureAwait(false);
        try
        {
            using var snapshot = await OpenApprovedAsync(reference, ct).ConfigureAwait(false);
            await Task.Run(() => PrepareWorkspace(directory, snapshot, reference.RelativePath, ct), ct).ConfigureAwait(false);
            return new ScriptWorkspaceLease(directory, gate);
        }
        catch { gate.Dispose(); throw; }
    }

    /// <summary>
    /// 从已保留归档准备可写副本并恢复声明的用户数据；归档内容始终保持不可变。
    /// 临时目录和旧目录备份放在同级，名称与项目名无关，比正式目录更短。
    /// </summary>
    private void PrepareWorkspace(string directory, ScriptRepositorySnapshot snapshot, string relativePath, CancellationToken ct)
    {
        var parent = Path.GetDirectoryName(directory)!;
        var temporary = Path.Combine(parent, ShortId.NewTemporaryName());
        var backup = Path.Combine(parent, ShortId.NewTemporaryName(".old"));
        try
        {
            Directory.CreateDirectory(temporary);
            // 项目文件去掉 repo/<相对路径> 前缀；同一提交的公共包保留 packages/ 前缀，动态导入不能读取更新后的包。
            if (ExtractFiles(snapshot, "repo/" + relativePath, temporary, false, ct) == 0)
                throw new IOException("资源目录为空，不能准备空项目。");
            if (snapshot.DirectoryExists("packages"))
                ExtractFiles(snapshot, "packages", temporary, true, ct);
            var manifest = ValidateJavaScriptProject(temporary);
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
