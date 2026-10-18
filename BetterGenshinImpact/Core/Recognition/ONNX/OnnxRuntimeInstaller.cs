using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public interface IOnnxRuntimeInstaller
{
    string PluginsDirectory { get; }
    RuntimeInstallation Inspect(InferenceDeviceType engine);
    Task<IReadOnlyList<RuntimeDownloadPackage>> GetDownloadPlanAsync(InferenceDeviceType engine, bool dependencies, CancellationToken token);
    Task InstallAsync(InferenceDeviceType engine, IReadOnlyList<RuntimeDownloadPackage> packages, IProgress<RuntimeDownloadProgress>? progress, CancellationToken token);
    Task CleanupInstalledDownloadsAsync(CancellationToken token);
}

public sealed class OnnxRuntimeInstaller : IOnnxRuntimeInstaller, IDisposable
{
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _installationLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    public string BaseDirectory { get; }
    public string PluginsDirectory => Path.Combine(BaseDirectory, "Plugins", "OnnxRuntime");

    public OnnxRuntimeInstaller(string baseDirectory, HttpClient? client = null)
    {
        BaseDirectory = Path.GetFullPath(baseDirectory);
        _ownsClient = client == null;
        _client = client ?? new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public RuntimeInstallation Inspect(InferenceDeviceType engine) => OnnxRuntimePackageCatalog.Inspect(BaseDirectory, engine);

    public async Task<IReadOnlyList<RuntimeDownloadPackage>> GetDownloadPlanAsync(InferenceDeviceType engine,
        bool dependencies, CancellationToken token)
    {
        if (engine == InferenceDeviceType.Cpu) return [];
        if (!dependencies)
        {
            var id = engine switch
            {
                InferenceDeviceType.DirectML => "Microsoft.ML.OnnxRuntime.DirectML",
                InferenceDeviceType.OpenVINO => "Intel.ML.OnnxRuntime.OpenVino",
                _ => "Microsoft.ML.OnnxRuntime.Gpu.Windows"
            };
            var packages = new List<RuntimeDownloadPackage> { await GetNugetPackageAsync(id, OnnxRuntimeLoader.RequiredVersion, token) };
            if (engine == InferenceDeviceType.DirectML) packages.Add(await GetNugetPackageAsync("Microsoft.AI.DirectML", "1.15.4", token));
            return packages;
        }
        if (engine != InferenceDeviceType.CUDA && engine != InferenceDeviceType.TensorRT) return [];
        var directory = Inspect(engine).Directory;
        bool Missing(string prefix) => OnnxRuntimePackageCatalog.DependencyFiles(engine)
            .Where(file => file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Any(file => !File.Exists(Path.Combine(directory, file)));
        var result = new List<RuntimeDownloadPackage>();
        var cuda = await ReadJsonAsync("https://developer.download.nvidia.com/compute/cuda/redist/redistrib_12.8.0.json", token);
        foreach (var (component, prefix) in new[] { ("cuda_cudart", "cudart"), ("libcublas", "cublas"), ("cuda_nvrtc", "nvrtc"), ("libcufft", "cufft"), ("libcurand", "curand") })
        {
            if (!Missing(prefix)) continue;
            var entry = cuda[component]!;
            result.Add(GetNvidiaPackage("cuda", component, entry.Value<string>("version")!, entry["windows-x86_64"]!));
        }
        if (Missing("cudnn"))
        {
            var cudnn = await ReadJsonAsync("https://developer.download.nvidia.com/compute/cudnn/redist/redistrib_9.8.0.json", token);
            var entry = cudnn["cudnn"]!;
            result.Add(GetNvidiaPackage("cudnn", "cuDNN", entry.Value<string>("version")!, entry["windows-x86_64"]!["cuda12"]!));
        }
        if (engine == InferenceDeviceType.TensorRT && (Missing("nvinfer") || Missing("nvonnxparser")))
            result.Add(new("TensorRT 10.8.0.43", new(OnnxRuntimePackageCatalog.TensorRtUrl), 1844107899, 2_507_893_528, null, "SHA256"));
        return result;
    }

    public async Task InstallAsync(InferenceDeviceType engine, IReadOnlyList<RuntimeDownloadPackage> packages,
        IProgress<RuntimeDownloadProgress>? progress, CancellationToken token)
    {
        if (packages.Count == 0) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        token = linked.Token;
        await _installationLock.WaitAsync(token);
        var root = OnnxRuntimePackageCatalog.EngineDirectory(PluginsDirectory, engine);
        // 临时目录只用于保证下载和解压完整，安装后仍只有引擎的固定目录。
        var staging = Path.Combine(PluginsDirectory, $".{engine}.staging-{Guid.NewGuid():N}");
        var backup = Path.Combine(PluginsDirectory, $".{engine}.backup-{Guid.NewGuid():N}");
        var completedArchives = new List<string>();
        try
        {
            Directory.CreateDirectory(PluginsDirectory);
            var required = packages.Sum(p => p.DownloadBytes + p.InstalledBytes);
            var disk = new DriveInfo(Path.GetPathRoot(root)!);
            if (disk.AvailableFreeSpace < required) throw new IOException($"磁盘空间不足，预计需要 {required / 1073741824d:F1} GB。");
            Directory.CreateDirectory(staging);
            var existing = Inspect(engine).Directory;
            if (Directory.Exists(existing))
                foreach (var file in Directory.EnumerateFiles(existing))
                {
                    token.ThrowIfCancellationRequested();
                    if (Path.GetExtension(file).Equals(".dll", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(file).Equals(".xml", StringComparison.OrdinalIgnoreCase))
                        File.Copy(file, Path.Combine(staging, Path.GetFileName(file)), true);
                }
            var downloader = new RuntimePackageDownloader(_client);
            foreach (var package in packages)
            {
                var archive = await downloader.DownloadAsync(package, Path.Combine(PluginsDirectory, ".downloads"), progress, token);
                completedArchives.Add(archive);
                progress?.Report(new($"正在解压并校验 {package.Name}", 0, 0));
                try { await RuntimePackageDownloader.ExtractAsync(archive, staging, path => IncludeFile(engine, package, path), token); }
                catch (InvalidDataException)
                {
                    // 包文件损坏后不能反复复用同一个下载缓存。
                    File.Delete(archive);
                    throw;
                }
            }
            foreach (var file in OnnxRuntimePackageCatalog.ProviderFiles(engine))
                if (!File.Exists(Path.Combine(staging, file))) throw new InvalidDataException($"安装包缺少 {file}。");
            token.ThrowIfCancellationRequested();

            // 最终目录名保持不变。先把旧目录改名为备份，再提交暂存目录；提交失败时恢复旧目录。
            // 若文件被当前进程加载，由 IO 异常提示用户重启后重试。
            CommitRuntimeDirectory(staging, root, backup);
            // 完整安装后再清理完整包；取消或失败时保留缓存供重试、续传。
            foreach (var archive in completedArchives) TryDeleteDownload(archive);
            TryRemoveEmptyDownloadDirectory();
            progress?.Report(new("下载完成，已重新识别运行库；应用设置后重启验证。", 1, 1));
        }
        catch (InvalidDataException)
        {
            // 包内容损坏或文件清单不完整时不保留缓存，避免下次继续复用错误包。
            foreach (var archive in completedArchives) TryDeleteDownload(archive);
            TryRemoveEmptyDownloadDirectory();
            throw;
        }
        finally
        {
            // 只删除本次安装的暂存目录。回滚失败时保留备份目录，避免再次破坏可恢复内容。
            try
            {
                if (Directory.Exists(staging) && IsDirectChildOf(staging, PluginsDirectory))
                    Directory.Delete(staging, true);
            }
            finally { _installationLock.Release(); }
        }
    }

    public async Task CleanupInstalledDownloadsAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _installationLock.WaitAsync(linked.Token);
        try
        {
            foreach (var engine in Enum.GetValues<InferenceDeviceType>().Where(engine => engine != InferenceDeviceType.Cpu))
            {
                linked.Token.ThrowIfCancellationRequested();
                CleanupAbandonedInstallationDirectories(engine);
            }

            var cache = Path.Combine(PluginsDirectory, ".downloads");
            if (Directory.Exists(cache))
            {
                foreach (var path in Directory.EnumerateFiles(cache, "*.zip"))
                {
                    linked.Token.ThrowIfCancellationRequested();
                    // 兼容之前留下的已安装下载包，只清理与当前安装内容完全一致的包。
                    var key = Path.GetFileNameWithoutExtension(path);
                    if (key.Length != 64 || !key.All(Uri.IsHexDigit)) continue;
                    try
                    {
                        if (await MatchesInstalledFilesAsync(path, linked.Token)) TryDeleteDownload(path);
                    }
                    catch (InvalidDataException) { /* 无法识别的包交由下载校验处理。 */ }
                    catch (IOException) { /* 文件被占用时保留，下次刷新再清理。 */ }
                    catch (UnauthorizedAccessException) { /* 缓存清理不能阻止设备识别。 */ }
                }
            }
            TryRemoveEmptyDownloadDirectory();
        }
        finally { _installationLock.Release(); }
    }

    private void CleanupAbandonedInstallationDirectories(InferenceDeviceType engine)
    {
        if (!Directory.Exists(PluginsDirectory)) return;
        foreach (var directory in Directory.EnumerateDirectories(PluginsDirectory, $".{engine}.staging-*"))
        {
            try { if (IsDirectChildOf(directory, PluginsDirectory)) Directory.Delete(directory, true); }
            catch (IOException) { /* 清理失败不能影响当前安装。 */ }
            catch (UnauthorizedAccessException) { /* 清理失败不能影响当前安装。 */ }
        }
        // 固定目录存在时，备份已经不再承担回滚职责；无法删除则留待下次刷新。
        var root = OnnxRuntimePackageCatalog.EngineDirectory(PluginsDirectory, engine);
        if (!Directory.Exists(root)) return;
        foreach (var directory in Directory.EnumerateDirectories(PluginsDirectory, $".{engine}.backup-*"))
        {
            try { if (IsDirectChildOf(directory, PluginsDirectory)) Directory.Delete(directory, true); }
            catch (IOException) { /* 清理失败不能影响当前安装。 */ }
            catch (UnauthorizedAccessException) { /* 清理失败不能影响当前安装。 */ }
        }
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

        if (!originalMoved) return;
        try { Directory.Delete(backup, true); }
        catch (IOException) { /* 旧运行库可能仍被其他进程占用，留待下次刷新清理。 */ }
        catch (UnauthorizedAccessException) { /* 新目录已经提交，备份留待下次刷新清理。 */ }
    }

    private static bool IsDirectChildOf(string path, string parent)
        => string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(parent),
            StringComparison.OrdinalIgnoreCase);

    private async Task<bool> MatchesInstalledFilesAsync(string path, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(path);
        var nuget = new RuntimeDownloadPackage("cache", new Uri("https://api.nuget.org/"), 0, 0, null, "SHA256", "cache");
        foreach (var engine in Enum.GetValues<InferenceDeviceType>().Where(e => e != InferenceDeviceType.Cpu))
        {
            var directory = Inspect(engine).Directory;
            var entries = archive.Entries.Where(e => IncludeFile(engine, nuget, e.FullName.Replace('\\', '/'))).ToArray();
            if (entries.Length == 0) continue;
            var matched = true;
            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                var file = Path.Combine(directory, Path.GetFileName(entry.FullName));
                if (!File.Exists(file) || new FileInfo(file).Length != entry.Length) { matched = false; break; }
                await using var input = entry.Open();
                await using var installed = File.OpenRead(file);
                var downloadedHash = await SHA256.HashDataAsync(input, token);
                var installedHash = await SHA256.HashDataAsync(installed, token);
                if (!CryptographicOperations.FixedTimeEquals(downloadedHash, installedHash))
                { matched = false; break; }
            }
            if (matched) return true;
        }
        return false;
    }

    private void TryDeleteDownload(string path)
    {
        // 校验绝对路径后，仅删除下载缓存中的单个归档，不触及运行库目录或续传文件。
        var cache = Path.GetFullPath(Path.Combine(PluginsDirectory, ".downloads"));
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), cache, StringComparison.OrdinalIgnoreCase)) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void TryRemoveEmptyDownloadDirectory()
    {
        var cache = Path.GetFullPath(Path.Combine(PluginsDirectory, ".downloads"));
        if (!cache.StartsWith(Path.GetFullPath(PluginsDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
        try { if (Directory.Exists(cache) && !Directory.EnumerateFileSystemEntries(cache).Any()) Directory.Delete(cache); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool IncludeFile(InferenceDeviceType engine, RuntimeDownloadPackage package, string path)
    {
        var name = Path.GetFileName(path);
        if (package.NugetId == "Microsoft.AI.DirectML") return path.Contains("x64", StringComparison.OrdinalIgnoreCase) && name.Equals("DirectML.dll", StringComparison.OrdinalIgnoreCase);
        if (package.NugetId != null)
        {
            if (!path.StartsWith("runtimes/win-x64/native/", StringComparison.OrdinalIgnoreCase)) return false;
            if (engine == InferenceDeviceType.OpenVINO) return path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
            return OnnxRuntimePackageCatalog.ProviderFiles(engine).Contains(name, StringComparer.OrdinalIgnoreCase);
        }
        return path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && (path.Contains("/bin/", StringComparison.OrdinalIgnoreCase) || path.Contains("/lib/", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<RuntimeDownloadPackage> GetNugetPackageAsync(string id, string version, CancellationToken token)
    {
        var lower = id.ToLowerInvariant();
        var registration = await ReadJsonAsync($"https://api.nuget.org/v3/registration5-gz-semver2/{lower}/{version}.json", token);
        var catalogUrl = new Uri(registration.Value<string>("catalogEntry")!);
        if (catalogUrl.Scheme != "https" || catalogUrl.Host != "api.nuget.org") throw new InvalidDataException("NuGet 元数据地址无效。");
        var catalog = await ReadJsonAsync(catalogUrl.AbsoluteUri, token);
        var entry = catalog;
        var size = entry.Value<long>("packageSize");
        var algorithm = entry.Value<string>("packageHashAlgorithm")!;
        if (algorithm != "SHA512") throw new InvalidDataException("NuGet 包摘要算法不符合预期。");
        return new($"{id} {version}", new($"https://api.nuget.org/v3-flatcontainer/{lower}/{version}/{lower}.{version}.nupkg"), size, size * 4,
            entry.Value<string>("packageHash"), algorithm, id);
    }

    private static RuntimeDownloadPackage GetNvidiaPackage(string product, string component, string version, JToken entry)
    {
        var size = long.Parse(entry["size"]!.ToString(), System.Globalization.CultureInfo.InvariantCulture);
        var url = new Uri($"https://developer.download.nvidia.com/compute/{product}/redist/{entry.Value<string>("relative_path")}");
        if (url.Host != "developer.download.nvidia.com" || url.Scheme != "https") throw new InvalidDataException("NVIDIA 下载地址无效。");
        return new($"{component} {version}", url, size, size * 4, entry.Value<string>("sha256"), "SHA256");
    }

    private async Task<JObject> ReadJsonAsync(string url, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await _client.GetAsync(url, timeout.Token);
        response.EnsureSuccessStatusCode();
        return JObject.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
    }

    public async Task StopAsync()
    {
        _lifetime.Cancel();
        await _installationLock.WaitAsync();
        _installationLock.Release();
    }

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
        _lifetime.Dispose();
        _installationLock.Dispose();
    }
}
