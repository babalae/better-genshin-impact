using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Infrastructure.Network.Download;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Core.Recognition.OpenVino;

public interface IOpenVinoRuntimeInstaller
{
    string RuntimeDirectory { get; }
    RuntimeInstallation Inspect();
    Task<IReadOnlyList<RuntimeDownloadPackage>> GetDownloadPlanAsync(CancellationToken token);
    Task InstallAsync(IReadOnlyList<RuntimeDownloadPackage> packages, IProgress<RuntimeDownloadProgress>? progress, CancellationToken token);
    Task CleanupInstalledDownloadsAsync(CancellationToken token);
}

/// <summary>
///     OpenVINO 独立后端的运行库安装器：从 NuGet 下载官方 OpenVINO 运行包并解压到 Plugins/OpenVINO。
///     机制（下载服务集成、暂存-备份-回滚提交、CRC 校验）与 OnnxRuntimeInstaller 同构；
///     独立实现而不共用引擎 switch，是因为两者从"按引擎多包"变为"单固定包"，清单与过滤器完全不同。
/// </summary>
public sealed class OpenVinoRuntimeInstaller : IOpenVinoRuntimeInstaller, IDisposable
{
    /// <summary>运行库版本：与 csproj 中的 JYPPX.OpenVINO.CSharp.API 版本配套（3.3.x ↔ OpenVINO 2026.3.x）。</summary>
    public const string RuntimeVersion = "2026.3.1";

    private const string RuntimePackageId = "OpenVINO.runtime.win";

    private readonly string _pluginsDirectory;
    private readonly IDownloadService _downloads;
    private readonly HttpClient _client;
    private readonly SemaphoreSlim _installationLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();

    public OpenVinoRuntimeInstaller(string baseDirectory, IDownloadService downloads, HttpClient client)
    {
        _pluginsDirectory = Path.Combine(baseDirectory, "Plugins");
        _downloads = downloads;
        _client = client;
    }

    public string RuntimeDirectory => OpenVinoRuntime.RuntimeDirectory;

    public RuntimeInstallation Inspect()
    {
        var missing = OpenVinoRuntime.FirstMissingFile();
        if (missing == null)
            return new(InferenceInstallationState.Installed, OpenVinoRuntime.RuntimeDirectory,
                $"OpenVINO {RuntimeVersion} 运行库已就绪。");
        return new(InferenceInstallationState.Missing, OpenVinoRuntime.RuntimeDirectory,
            $"OpenVINO {RuntimeVersion} 运行库未安装：缺少 {missing}。");
    }

    public async Task<IReadOnlyList<RuntimeDownloadPackage>> GetDownloadPlanAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        if (OpenVinoRuntime.IsInstalled()) return [];
        return [await GetNugetPackageAsync(RuntimePackageId, RuntimeVersion, linked.Token)];
    }

    public async Task InstallAsync(IReadOnlyList<RuntimeDownloadPackage> packages,
        IProgress<RuntimeDownloadProgress>? progress, CancellationToken token)
    {
        if (packages.Count == 0) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        token = linked.Token;
        await _installationLock.WaitAsync(token);
        var root = OpenVinoRuntime.RuntimeDirectory;
        var staging = Path.Combine(_pluginsDirectory, $".openvino-runtime.staging-{Guid.NewGuid():N}");
        var backup = Path.Combine(_pluginsDirectory, $".openvino-runtime.backup-{Guid.NewGuid():N}");
        var completedArchives = new List<string>();
        try
        {
            Directory.CreateDirectory(_pluginsDirectory);
            var required = packages.Sum(p => p.DownloadBytes + p.InstalledBytes);
            var disk = new DriveInfo(Path.GetPathRoot(root)!);
            if (disk.AvailableFreeSpace < required) throw new IOException($"磁盘空间不足，预计需要 {required / 1073741824d:F1} GB。");
            Directory.CreateDirectory(staging);
            // 旧目录中用户手工放置的 DLL 原样保留进暂存。
            if (Directory.Exists(root))
                foreach (var file in Directory.EnumerateFiles(root))
                {
                    token.ThrowIfCancellationRequested();
                    var extension = Path.GetExtension(file);
                    if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
                        File.Copy(file, Path.Combine(staging, Path.GetFileName(file)), true);
                }

            foreach (var package in packages)
            {
                var archive = await DownloadPackageAsync(package, progress, token);
                completedArchives.Add(archive);
                progress?.Report(new($"正在解压并校验 {package.Name}", 0, 0));
                try { await RuntimePackageDownloader.ExtractAsync(archive, staging, IncludeFile, token); }
                catch (InvalidDataException)
                {
                    // 包文件损坏后不能反复复用同一个下载缓存。
                    File.Delete(archive);
                    throw;
                }
            }

            foreach (var file in OpenVinoRuntime.RequiredFiles)
                if (!File.Exists(Path.Combine(staging, file))) throw new InvalidDataException($"安装包缺少 {file}。");
            token.ThrowIfCancellationRequested();

            CommitRuntimeDirectory(staging, root, backup);
            foreach (var archive in completedArchives) TryDeleteDownload(archive);
            progress?.Report(new("OpenVINO 运行库已就绪。", 1, 1));
        }
        catch (InvalidDataException)
        {
            foreach (var archive in completedArchives) TryDeleteDownload(archive);
            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging) && IsDirectChildOf(staging, _pluginsDirectory))
                    Directory.Delete(staging, true);
            }
            finally { _installationLock.Release(); }
        }
    }

    public Task CleanupInstalledDownloadsAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        if (!Directory.Exists(_pluginsDirectory)) return Task.CompletedTask;
        foreach (var pattern in new[] { ".openvino-runtime.staging-*", ".openvino-runtime.backup-*" })
        {
            foreach (var directory in Directory.EnumerateDirectories(_pluginsDirectory, pattern))
            {
                linked.Token.ThrowIfCancellationRequested();
                // 根目录已存在时备份不再承担回滚职责；无法删除则留待下次刷新。
                if (pattern.Contains("backup") && !Directory.Exists(RuntimeDirectory)) continue;
                try { if (IsDirectChildOf(directory, _pluginsDirectory)) Directory.Delete(directory, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        return Task.CompletedTask;
    }

    private static void CommitRuntimeDirectory(string staging, string root, string backup)
    {
        var originalMoved = false;
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Move(root, backup);
                originalMoved = true;
            }
            Directory.Move(staging, root);
        }
        catch (Exception commitError) when (commitError is IOException or UnauthorizedAccessException)
        {
            if (!originalMoved) throw;
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
                if (Directory.Exists(backup)) Directory.Move(backup, root);
            }
            catch (Exception rollbackError) when (rollbackError is IOException or UnauthorizedAccessException)
            {
                throw new IOException($"运行库提交和回滚均失败，旧运行库备份保留在：{backup}",
                    new AggregateException(commitError, rollbackError));
            }
            throw;
        }
        finally
        {
            if (originalMoved && Directory.Exists(backup))
            {
                try { Directory.Delete(backup, true); }
                catch (IOException) { /* 占用留待下次清理 */ }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static bool IsDirectChildOf(string directory, string parent)
    {
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetDirectoryName(fullDirectory).Equals(fullParent, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>只保留运行时原生目录下的 DLL（前端 / 插件 / TBB），跳过许可文档等无关内容。</summary>
    private static bool IncludeFile(string path)
        => path.StartsWith("runtimes/win-x64/native/", StringComparison.OrdinalIgnoreCase)
           && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    /// <summary>通过下载服务下载一个安装包，返回下载目录中的文件路径；同一任务也出现在下载中心。</summary>
    private async Task<string> DownloadPackageAsync(RuntimeDownloadPackage package,
        IProgress<RuntimeDownloadProgress>? progress, CancellationToken token)
    {
        var task = _downloads.Start(new DownloadRequest(package.Url, package.Name, DownloadCategories.OnnxRuntime)
        {
            ExpectedSize = package.DownloadBytes > 0 ? package.DownloadBytes : null,
            ExpectedHash = package.Hash is null ? null : new DownloadHash(package.HashAlgorithm, package.Hash),
            Origin = "硬件加速设置",
        }, token);
        task.ProgressChanged += (_, p) =>
        {
            var message = p.State switch
            {
                DownloadState.Queued => $"等待其他下载完成：{package.Name}",
                DownloadState.Downloading => $"正在下载 {package.Name}",
                DownloadState.Verifying => $"正在校验 {package.Name}",
                _ => null,
            };
            if (message != null) progress?.Report(new(message, p.ReceivedBytes, p.TotalBytes ?? 0));
        };
        return await task.Completion;
    }

    private void TryDeleteDownload(string path)
    {
        var cache = Path.GetFullPath(_downloads.GetDirectory(DownloadCategories.OnnxRuntime));
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), cache, StringComparison.OrdinalIgnoreCase)) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>NuGet 包元数据读取（SHA512 摘要校验），与 OnnxRuntimeInstaller 同构；仅面向 OpenVINO 运行包。</summary>
    private async Task<RuntimeDownloadPackage> GetNugetPackageAsync(string id, string version, CancellationToken token)
    {
        var lower = id.ToLowerInvariant();
        var registration = JObject.Parse(await ReadTextAsync(
            $"https://api.nuget.org/v3/registration5-gz-semver2/{lower}/{version}.json", token));
        var catalogUrl = new Uri(registration.Value<string>("catalogEntry")!);
        if (catalogUrl.Scheme != "https" || catalogUrl.Host != "api.nuget.org") throw new InvalidDataException("NuGet 元数据地址无效。");
        var entry = JObject.Parse(await ReadTextAsync(catalogUrl.AbsoluteUri, token));
        var size = entry.Value<long>("packageSize");
        if (size <= 0) throw new InvalidDataException("NuGet 包大小无效。");
        var algorithm = entry.Value<string>("packageHashAlgorithm")!;
        if (algorithm != "SHA512") throw new InvalidDataException("NuGet 包摘要算法不符合预期。");
        var hash = entry.Value<string>("packageHash");
        if (hash == null || !Convert.TryFromBase64String(hash, new byte[64], out var hashLength) || hashLength != 64)
            throw new InvalidDataException("NuGet 包 SHA512 摘要无效。");
        return new($"{id} {version}", new($"https://api.nuget.org/v3-flatcontainer/{lower}/{version}/{lower}.{version}.nupkg"),
            size, size * 4, hash, algorithm, id);
    }

    private async Task<string> ReadTextAsync(string url, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await _client.GetAsync(url, timeout.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(timeout.Token);
    }

    public void StopAsync() => _lifetime.Cancel();
    public void Dispose() => _lifetime.Dispose();
}
