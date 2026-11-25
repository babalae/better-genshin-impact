using BetterGenshinImpact.Core.Script;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Music.Service;

/// <summary>
/// 提供曲谱统计和仓库曲谱同步。
/// </summary>
public interface IMusicScoreRepoSyncService
{
    /// <summary>
    /// 读取索引统计；旧仓库缺少索引时返回空。
    /// </summary>
    MusicScoreRepoStatistics? ReadStatistics();

    /// <summary>
    /// 同步脚本仓库 assets/music/scores 到曲谱目录的「仓库曲谱」子目录，返回新增数量。
    /// </summary>
    Task<int> SyncAsync(string musicFolder, CancellationToken cancellationToken);
}

/// <summary>
/// 仓库乐谱数量及格式和乐器分布。
/// </summary>
public sealed record MusicScoreRepoStatistics(
    [property: JsonProperty(Required = Required.Always)] int Count,
    IReadOnlyDictionary<string, int>? ByType,
    IReadOnlyDictionary<string, int>? ByInstrument);

/// <summary>
/// 只更新和清理本服务创建且内容未被修改的副本。用户曲谱按内容去重。
/// </summary>
public sealed class MusicScoreRepoSyncService : IMusicScoreRepoSyncService
{
    /// <summary>
    /// 曲库中存放仓库副本的目录名称。
    /// </summary>
    private const string RepoScoreDirName = "仓库曲谱";
    /// <summary>
    /// 曲谱在资源索引中的目录。
    /// </summary>
    private const string RepoScoreRelativeDir = "music/scores";
    /// <summary>
    /// 记录程序创建的曲谱副本及其摘要的文件。
    /// </summary>
    private const string ManifestName = ".bettergi-repo-scores.manifest";

    /// <summary>
    /// 记录资源读取及同步结果。
    /// </summary>
    private readonly ILogger<MusicScoreRepoSyncService> _logger = App.GetLogger<MusicScoreRepoSyncService>();
    /// <summary>
    /// 串行执行对曲库目录的同步写入。
    /// </summary>
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    /// <inheritdoc/>
    public MusicScoreRepoStatistics? ReadStatistics()
    {
        var json = ScriptRepoUpdater.Instance.GetAssetIndexJson();
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            var statistics = JObject.Parse(json)["musicScoreStats"]
                ?.ToObject<MusicScoreRepoStatistics>();
            return statistics is { Count: >= 0 } ? statistics : null;
        }
        catch (JsonException e)
        {
            _logger.LogDebug(e, "读取仓库曲谱统计失败");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<int> SyncAsync(string musicFolder, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(musicFolder) || !Directory.Exists(musicFolder))
        {
            return 0;
        }

        await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => SyncCore(musicFolder, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    /// <summary>
    /// 将索引中的曲谱同步到选定目录。
    /// </summary>
    private int SyncCore(string musicFolder, CancellationToken cancellationToken)
    {
        var repoFiles = ScriptRepoUpdater.Instance.ReadAssetFiles(RepoScoreRelativeDir, ".json");
        if (repoFiles is not { Count: > 0 })
        {
            return 0;
        }

        var desired = new Dictionary<string, (byte[] Content, string Hash)>(StringComparer.OrdinalIgnoreCase);
        var repoHashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in repoFiles.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(file.Path);
            if (!IsScoreFileName(fileName))
            {
                continue;
            }

            var hash = Convert.ToHexString(SHA256.HashData(file.Content));
            if (repoHashes.Add(hash) && !desired.TryAdd(fileName, (file.Content, hash)))
            {
                throw new InvalidDataException($"仓库中存在同名曲谱：{fileName}");
            }
        }

        if (desired.Count == 0)
        {
            return 0;
        }

        musicFolder = Path.GetFullPath(musicFolder);
        var targetDir = Path.Combine(musicFolder, RepoScoreDirName);
        var manifestPath = Path.Combine(targetDir, ManifestName);
        if (IsReparsePoint(targetDir) || IsReparsePoint(manifestPath))
        {
            _logger.LogWarning("曲谱同步目标包含链接，已跳过：{Dir}", targetDir);
            return 0;
        }

        var managed = File.Exists(manifestPath)
            ? new Dictionary<string, string>(
                JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(manifestPath))
                    ?? throw new InvalidDataException("仓库曲谱清单为空"),
                StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var managedBeforeSync = managed.Keys.ToArray();

        var availableHashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(musicFolder, "*.json", new EnumerationOptions
                 {
                     RecurseSubdirectories = true,
                     IgnoreInaccessible = true,
                     AttributesToSkip = FileAttributes.ReparsePoint
                 }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hash = TryGetFileHash(file);
            if (hash != null && !(string.Equals(Path.GetDirectoryName(file), targetDir, StringComparison.OrdinalIgnoreCase)
                                  && managed.TryGetValue(Path.GetFileName(file), out var previousHash)
                                  && previousHash == hash))
            {
                availableHashes.Add(hash);
            }
        }

        Directory.CreateDirectory(targetDir);
        var added = 0;
        var manifestChanged = false;
        try
        {
            foreach (var (fileName, score) in desired)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var targetPath = Path.Combine(targetDir, fileName);
                var attributes = GetAttributes(targetPath);
                var currentHash = TryGetFileHash(targetPath);
                var isManaged = currentHash != null && managed.TryGetValue(fileName, out var previousHash)
                                                    && previousHash == currentHash;
                if (attributes != null && !isManaged)
                {
                    // 同名文件属于用户，保留文件并撤销旧的管理记录。
                    manifestChanged |= managed.Remove(fileName);
                    if (currentHash != null)
                    {
                        availableHashes.Add(currentHash);
                    }
                    continue;
                }

                if (currentHash == score.Hash || (attributes == null && availableHashes.Contains(score.Hash)))
                {
                    availableHashes.Add(score.Hash);
                    continue;
                }

                WriteAtomically(targetPath, score.Content, overwrite: isManaged);
                managed[fileName] = score.Hash;
                availableHashes.Add(score.Hash);
                manifestChanged = true;
                added++;
            }

            // 先写好所有新曲谱，再清理旧名称，避免改名时丢失唯一副本。
            foreach (var fileName in managedBeforeSync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsScoreFileName(fileName) || desired.ContainsKey(fileName))
                {
                    continue;
                }

                var targetPath = Path.Combine(targetDir, fileName);
                var currentHash = TryGetFileHash(targetPath);
                if (currentHash != null && currentHash == managed[fileName])
                {
                    if (repoHashes.Contains(currentHash) && !availableHashes.Contains(currentHash))
                    {
                        // 新名称被用户文件占用，旧副本仍是这首曲谱的唯一可用版本。
                        continue;
                    }

                    File.Delete(targetPath);
                }
                else if (currentHash == null && GetAttributes(targetPath) != null)
                {
                    continue;
                }

                manifestChanged |= managed.Remove(fileName);
            }
        }
        finally
        {
            if (manifestChanged)
            {
                WriteAtomically(manifestPath, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(managed)),
                    overwrite: File.Exists(manifestPath));
            }
        }

        if (added > 0)
        {
            _logger.LogInformation("已从脚本仓库同步 {Count} 首乐谱到 {Dir}", added, targetDir);
        }

        return added;
    }

    /// <summary>
    /// 判断资源能否作为本地 JSON 曲谱文件保存。
    /// </summary>
    private static bool IsScoreFileName(string fileName)
    {
        return fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
               && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    /// <summary>
    /// 检查路径是否为链接或目录联接。
    /// </summary>
    private static bool IsReparsePoint(string path)
    {
        return GetAttributes(path) is { } attributes && (attributes & FileAttributes.ReparsePoint) != 0;
    }

    /// <summary>
    /// 读取路径属性，路径不存在时返回空。
    /// </summary>
    private static FileAttributes? GetAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// 读取普通文件的 SHA-256，文件不可读时返回空。
    /// </summary>
    private static string? TryGetFileHash(string path)
    {
        try
        {
            if (GetAttributes(path) is not { } attributes ||
                (attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 先写入同目录的临时文件，再替换目标。
    /// </summary>
    private static void WriteAtomically(string path, byte[] content, bool overwrite)
    {
        var stagingPath = Path.Combine(Path.GetDirectoryName(path)!, $".bettergi-{Guid.NewGuid():N}.part");
        var created = false;
        try
        {
            using (var stream = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                stream.Write(content);
            }

            File.Move(stagingPath, path, overwrite);
        }
        catch
        {
            if (created)
            {
                File.Delete(stagingPath);
            }

            throw;
        }
    }
}
