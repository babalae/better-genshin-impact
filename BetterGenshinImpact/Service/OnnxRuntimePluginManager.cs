using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Helpers.Http;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 下载和管理独立于应用发布包的 ONNX Runtime Plugin EP。
/// 动态内容固定存放在程序目录的 ort 文件夹，不回退到 User 目录。
/// </summary>
public sealed class OnnxRuntimePluginManager : IOnnxRuntimePluginManager
{
    private const long MaxPackageBytes = 1024L * 1024 * 1024;
    private const string OpenVinoPackageId = "intel.ml.onnxruntime.ep.openvino";
    private const string CudaReleasePageUrl =
        "https://github.com/microsoft/onnxruntime/releases/tag/plugin-ep-cuda/v0.1.0";

    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILogger<OnnxRuntimePluginManager> _logger;
    private readonly IConfigService _configService;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateSyncRoot = new();
    private readonly string _packagesRoot;
    private readonly string _downloadsRoot;
    private readonly string _statePath;
    private readonly string _catalogCachePath;
    private PluginStoreState _state;
    private IReadOnlyList<OnnxRuntimePluginDescriptor> _catalog;

    public OnnxRuntimePluginManager(
        ILogger<OnnxRuntimePluginManager> logger,
        IConfigService configService)
    {
        _logger = logger;
        _configService = configService;
        StorageRoot = Path.Combine(AppContext.BaseDirectory, "ort");
        _packagesRoot = Path.Combine(StorageRoot, "packages");
        _downloadsRoot = Path.Combine(StorageRoot, "downloads");
        _statePath = Path.Combine(StorageRoot, "state.json");
        _catalogCachePath = Path.Combine(StorageRoot, "catalog-cache.json");
        _httpClient = HttpClientFactory.GetClient("onnx-runtime-plugins", CreateHttpClient);

        IsStorageWritable = TryPrepareStorage(out var storageError);
        StorageError = storageError;
        _state = LoadState();
        TryCleanupPendingDirectories();
        _catalog = MergeCatalog(CreateBuiltInCatalog(), LoadCachedCatalog());
    }

    public string StorageRoot { get; }
    public bool IsStorageWritable { get; }
    public string StorageError { get; }

    public IReadOnlyList<OnnxRuntimePluginInfo> GetCurrentPackages()
    {
        lock (_stateSyncRoot)
        {
            _state = LoadState();
            return BuildPackageInfos(_catalog);
        }
    }

    public async Task<IReadOnlyList<OnnxRuntimePluginInfo>> RefreshCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var catalog = MergeCatalog(CreateBuiltInCatalog(), LoadCachedCatalog()).ToList();
            using var timeoutSource = new CancellationTokenSource(NetworkTimeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutSource.Token);

            await RefreshOpenVinoMetadataAsync(catalog[0], linkedSource.Token).ConfigureAwait(false);
            _catalog = catalog;

            if (IsStorageWritable)
            {
                using (AcquireStoreMutex())
                {
                    WriteJsonAtomically(_catalogCachePath,
                        new OnnxRuntimePluginCatalog { Packages = catalog });
                }
            }

            lock (_stateSyncRoot)
            {
                return BuildPackageInfos(_catalog);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("获取 Plugin EP 包信息超时。");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task InstallAsync(OnnxRuntimePluginDescriptor descriptor,
        IProgress<PluginDownloadProgress>? progress, CancellationToken cancellationToken = default)
    {
        if (!IsStorageWritable)
        {
            throw new IOException(StorageError);
        }

        if (!descriptor.IsPublished || string.IsNullOrWhiteSpace(descriptor.DownloadUrl))
        {
            throw new InvalidOperationException($"{descriptor.DisplayName} 的下载包尚未发布。");
        }

        ValidateDescriptorForInstall(descriptor);

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var downloadPath = Path.Combine(_downloadsRoot, $"{descriptor.Id}-{Guid.NewGuid():N}.part");
        var extractionPath = Path.Combine(_downloadsRoot, $"{descriptor.Id}-{Guid.NewGuid():N}.extracting");
        try
        {
            Directory.CreateDirectory(_downloadsRoot);
            await DownloadAndVerifyAsync(descriptor, downloadPath, progress, cancellationToken)
                .ConfigureAwait(false);

            Directory.CreateDirectory(extractionPath);
            ExtractPackage(descriptor, downloadPath, extractionPath);
            var entryPath = FindEntryLibrary(extractionPath, descriptor.EntryLibrary)
                            ?? throw new InvalidDataException(
                                $"包中缺少 Plugin EP 入口文件 {descriptor.EntryLibrary}。");

            if (Directory.EnumerateFiles(extractionPath, "onnxruntime.dll", SearchOption.AllDirectories).Any())
            {
                throw new InvalidDataException("Plugin EP 包不得包含 onnxruntime.dll。");
            }

            var finalDirectory = GetVersionDirectory(descriptor.Id, descriptor.Version);
            using (AcquireStoreMutex())
            {
                if (Directory.Exists(finalDirectory))
                {
                    var installedEntry = FindEntryLibrary(finalDirectory, descriptor.EntryLibrary);
                    if (installedEntry is null)
                    {
                        Directory.Delete(finalDirectory, true);
                    }
                    else
                    {
                        Directory.Delete(extractionPath, true);
                    }
                }

                if (!Directory.Exists(finalDirectory))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(finalDirectory)!);
                    Directory.Move(extractionPath, finalDirectory);
                }

                lock (_stateSyncRoot)
                {
                    _state = LoadState();
                    var installState = GetOrCreateInstallState(descriptor.Id);
                    if (installState.ActiveDescriptor is null &&
                        installState.Descriptor is not null &&
                        installState.ActiveVersion.Equals(installState.Descriptor.Version,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        installState.ActiveDescriptor = installState.Descriptor;
                    }

                    installState.Descriptor = descriptor;
                    installState.PendingVersion = descriptor.Version;
                    installState.PendingDescriptor = descriptor;
                    installState.PendingInstalledManually = false;
                    SaveState();
                }
            }

            _logger.LogInformation("[ONNX] 已安装 Plugin EP {Plugin} {Version}，等待重启启用。入口：{Entry}",
                descriptor.DisplayName, descriptor.Version, entryPath);
        }
        finally
        {
            TryDeleteFile(downloadPath);
            TryDeleteDirectory(extractionPath);
            _operationGate.Release();
        }
    }

    /// <summary>
    /// 登记用户自行下载并解压到指定目录的插件。手动内容由用户负责来源可信性，
    /// 这里仍会检查入口 DLL、目录边界和不得携带 ORT Core 等基本约束。
    /// </summary>
    public async Task RegisterManualInstallationAsync(OnnxRuntimePluginDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        if (!IsStorageWritable)
        {
            throw new IOException(StorageError);
        }

        ValidateDescriptorForInstall(descriptor);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = GetManualInstallDirectory(descriptor);
            var entryPath = FindEntryLibrary(directory, descriptor.EntryLibrary)
                            ?? throw new FileNotFoundException(
                                $"没有在手动安装目录中找到 {descriptor.EntryLibrary}。", directory);
            if (new FileInfo(entryPath).Length < 64 * 1024)
            {
                throw new InvalidDataException($"{descriptor.EntryLibrary} 文件尺寸异常，请重新解压官方包。");
            }

            if (Directory.EnumerateFiles(directory, "onnxruntime.dll", SearchOption.AllDirectories).Any())
            {
                throw new InvalidDataException("Plugin EP 目录不得包含 onnxruntime.dll，请仅放置插件包内容。");
            }

            using (AcquireStoreMutex())
            {
                lock (_stateSyncRoot)
                {
                    _state = LoadState();
                    var installState = GetOrCreateInstallState(descriptor.Id);
                    installState.Descriptor = descriptor;
                    installState.PendingVersion = descriptor.Version;
                    installState.PendingDescriptor = descriptor;
                    installState.PendingInstalledManually = true;
                    SaveState();
                }
            }

            _logger.LogInformation(
                "[ONNX] 已登记手动安装的 Plugin EP {Plugin} {Version}，等待重启启用。入口：{Entry}",
                descriptor.DisplayName, descriptor.Version, entryPath);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task UninstallAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        if (!IsStorageWritable)
        {
            throw new IOException(StorageError);
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using (AcquireStoreMutex())
            {
                var pluginDirectory = GetPluginDirectory(pluginId);
                lock (_stateSyncRoot)
                {
                    _state = LoadState();
                    if (_state.Plugins.TryGetValue(pluginId, out var installState))
                    {
                        installState.ActiveVersion = "";
                        installState.PendingVersion = "";
                        installState.ActiveDescriptor = null;
                        installState.PendingDescriptor = null;
                        installState.ActiveInstalledManually = false;
                        installState.PendingInstalledManually = false;
                    }

                    if (Directory.Exists(pluginDirectory))
                    {
                        try
                        {
                            Directory.Delete(pluginDirectory, true);
                        }
                        catch (IOException)
                        {
                            AddPendingDelete(pluginDirectory);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            AddPendingDelete(pluginDirectory);
                        }
                    }

                    SaveState();
                }
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public IReadOnlyList<OnnxRuntimePluginResolution> ResolveForStartup(InferenceDeviceType provider,
        HardwareAccelerationConfig.CudaRuntimeMajor cudaRuntime, HardwareAccelerationConfig? config = null)
    {
        string[] ids = provider switch
        {
            InferenceDeviceType.OpenVino => ["openvino"],
            InferenceDeviceType.Cuda when cudaRuntime == HardwareAccelerationConfig.CudaRuntimeMajor.Cuda12 =>
                ["cuda12"],
            InferenceDeviceType.Cuda when cudaRuntime == HardwareAccelerationConfig.CudaRuntimeMajor.Cuda13 =>
                ["cuda13"],
            InferenceDeviceType.Cuda => ["cuda13", "cuda12"],
            _ => []
        };

        var result = new List<OnnxRuntimePluginResolution>();
        lock (_stateSyncRoot)
        {
            _state = LoadState();
            foreach (var id in ids)
            {
                if (!_state.Plugins.TryGetValue(id, out var installState))
                {
                    continue;
                }

                var versions = new[]
                    {
                        (Version: installState.PendingVersion,
                            Descriptor: installState.PendingDescriptor ?? installState.Descriptor),
                        (Version: installState.ActiveVersion,
                            Descriptor: installState.ActiveDescriptor ?? installState.Descriptor)
                    }
                    .Where(item => !string.IsNullOrWhiteSpace(item.Version) && item.Descriptor is not null)
                    .DistinctBy(item => item.Version, StringComparer.OrdinalIgnoreCase);
                foreach (var item in versions)
                {
                    var version = item.Version;
                    var descriptor = item.Descriptor!;
                    if (!IsSafePathSegment(version) ||
                        !version.Equals(descriptor.Version, StringComparison.OrdinalIgnoreCase) ||
                        !IsTrustedInstalledDescriptor(descriptor) ||
                        !OnnxRuntimeDependencyChecker.Check(
                            descriptor, config ?? _configService.Get().HardwareAccelerationConfig).IsCompatible)
                    {
                        continue;
                    }

                    var directory = GetVersionDirectory(id, version);
                    var libraryPath = FindEntryLibrary(directory, descriptor.EntryLibrary);
                    if (libraryPath is null)
                    {
                        continue;
                    }

                    result.Add(new OnnxRuntimePluginResolution(
                        descriptor,
                        version,
                        libraryPath,
                        string.Equals(version, installState.PendingVersion, StringComparison.OrdinalIgnoreCase)));
                }
            }
        }

        return result;
    }

    public void MarkActivationSucceeded(OnnxRuntimePluginResolution resolution)
    {
        if (!IsStorageWritable)
        {
            return;
        }

        try
        {
            using (AcquireStoreMutex())
            {
                lock (_stateSyncRoot)
                {
                    _state = LoadState();
                    var installState = GetOrCreateInstallState(resolution.Descriptor.Id);
                    installState.Descriptor = resolution.Descriptor;
                    installState.ActiveVersion = resolution.Version;
                    installState.ActiveDescriptor = resolution.Descriptor;
                    installState.ActiveInstalledManually = resolution.IsPendingVersion
                        ? installState.PendingInstalledManually
                        : installState.ActiveInstalledManually;
                    if (string.Equals(installState.PendingVersion, resolution.Version,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        installState.PendingVersion = "";
                        installState.PendingDescriptor = null;
                        installState.PendingInstalledManually = false;
                    }

                    SaveState();
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "[ONNX] 无法保存 Plugin EP 激活状态");
        }
    }

    public string GetPluginDirectory(string pluginId)
    {
        if (!IsSafePathSegment(pluginId))
        {
            throw new ArgumentException("Plugin EP 标识包含非法路径字符。", nameof(pluginId));
        }

        return Path.Combine(_packagesRoot, pluginId);
    }

    public string GetManualInstallDirectory(OnnxRuntimePluginDescriptor descriptor)
    {
        ValidateDescriptorForInstall(descriptor);
        return GetVersionDirectory(descriptor.Id, descriptor.Version);
    }

    private async Task RefreshOpenVinoMetadataAsync(OnnxRuntimePluginDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        try
        {
            var indexUrl = $"https://api.nuget.org/v3-flatcontainer/{OpenVinoPackageId}/index.json";
            using var response = await _httpClient.GetAsync(indexUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            descriptor.SourceLatestVersion = document.RootElement.GetProperty("versions")
                .EnumerateArray()
                .Select(element => element.GetString())
                .Where(version => !string.IsNullOrWhiteSpace(version) && !version.Contains('-'))
                .OrderByDescending(version => ParseVersion(version!))
                .FirstOrDefault() ?? descriptor.Version;

            // NuGet.org 不提供与 nupkg 同路径的 .nupkg.sha512 文件。校验值位于
            // Registration Leaf 所指向的 Catalog Entry 中，字段为 packageHash。
            var registrationUrl =
                $"https://api.nuget.org/v3/registration5-semver1/{OpenVinoPackageId}/{descriptor.Version}.json";
            using var registrationResponse = await _httpClient.GetAsync(registrationUrl, cancellationToken)
                .ConfigureAwait(false);
            registrationResponse.EnsureSuccessStatusCode();
            await using var registrationStream = await registrationResponse.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var registrationDocument = await JsonDocument.ParseAsync(
                registrationStream, cancellationToken: cancellationToken).ConfigureAwait(false);

            var registrationRoot = registrationDocument.RootElement;
            var catalogEntryUrl = registrationRoot.GetProperty("catalogEntry").GetString();
            if (!IsOfficialNuGetApiUrl(catalogEntryUrl))
            {
                throw new InvalidDataException("OpenVINO NuGet 元数据未提供可信的 Catalog Entry 地址。");
            }

            using var catalogResponse = await _httpClient.GetAsync(catalogEntryUrl, cancellationToken)
                .ConfigureAwait(false);
            catalogResponse.EnsureSuccessStatusCode();
            await using var catalogStream = await catalogResponse.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var catalogDocument = await JsonDocument.ParseAsync(
                catalogStream, cancellationToken: cancellationToken).ConfigureAwait(false);

            var catalogRoot = catalogDocument.RootElement;
            var checksumAlgorithm = catalogRoot.GetProperty("packageHashAlgorithm").GetString();
            var checksum = catalogRoot.GetProperty("packageHash").GetString()?.Trim() ?? "";
            if (!string.Equals(checksumAlgorithm, "SHA512", StringComparison.OrdinalIgnoreCase) ||
                !IsValidSha512(checksum))
            {
                throw new InvalidDataException("OpenVINO NuGet 元数据未提供有效的 SHA-512 校验值。");
            }

            if (IsValidSha512(descriptor.Checksum) &&
                !string.Equals(descriptor.Checksum, checksum, StringComparison.Ordinal))
            {
                throw new InvalidDataException("OpenVINO NuGet 包校验值与内置可信值不一致。");
            }

            descriptor.ChecksumAlgorithm = "SHA512";
            descriptor.Checksum = checksum;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "[ONNX] 获取 OpenVINO NuGet 元数据失败，继续使用缓存或内置元数据");
        }
    }

    private async Task DownloadAndVerifyAsync(OnnxRuntimePluginDescriptor descriptor, string destination,
        IProgress<PluginDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        foreach (var url in new[] { descriptor.DownloadUrl, descriptor.FallbackDownloadUrl }
                     .Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            try
            {
                TryDeleteFile(destination);
                using var timeoutSource = new CancellationTokenSource(TimeSpan.FromMinutes(15));
                using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, timeoutSource.Token);
                using var response = await _httpClient.GetAsync(url,
                    HttpCompletionOption.ResponseHeadersRead, linkedSource.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength;
                if (totalBytes > MaxPackageBytes)
                {
                    throw new InvalidDataException("Plugin EP 包超过 1 GB 安全限制。");
                }

                await using var source = await response.Content.ReadAsStreamAsync(linkedSource.Token)
                    .ConfigureAwait(false);
                await using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write,
                                 FileShare.None, 1024 * 128,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var buffer = new byte[1024 * 128];
                    long received = 0;
                    while (true)
                    {
                        var read = await source.ReadAsync(buffer, linkedSource.Token).ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }

                        received += read;
                        if (received > MaxPackageBytes)
                        {
                            throw new InvalidDataException("Plugin EP 包超过 1 GB 安全限制。");
                        }

                        await target.WriteAsync(buffer.AsMemory(0, read), linkedSource.Token).ConfigureAwait(false);
                        progress?.Report(new PluginDownloadProgress(received, totalBytes));
                    }

                    await target.FlushAsync(linkedSource.Token).ConfigureAwait(false);
                }

                // Windows 下写入句柄使用 FileShare.None，必须先释放后才能重新打开文件进行校验。
                await VerifyChecksumAsync(descriptor, destination, linkedSource.Token).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lastException = exception;
                _logger.LogWarning(exception, "[ONNX] 从 {Url} 下载 Plugin EP 失败", url);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = new TimeoutException("Plugin EP 下载超时。", exception);
                _logger.LogWarning(exception, "[ONNX] 从 {Url} 下载 Plugin EP 超时", url);
            }
        }

        throw new InvalidOperationException($"下载 {descriptor.DisplayName} 失败。", lastException);
    }

    private static async Task VerifyChecksumAsync(OnnxRuntimePluginDescriptor descriptor, string filePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Checksum))
        {
            throw new InvalidDataException("包清单缺少校验值，已拒绝安装。");
        }

        var useSha512 = string.Equals(descriptor.ChecksumAlgorithm, "SHA512", StringComparison.OrdinalIgnoreCase);
        using var algorithm = IncrementalHash.CreateHash(useSha512
            ? HashAlgorithmName.SHA512
            : HashAlgorithmName.SHA256);
        await using var stream = File.OpenRead(filePath);
        var buffer = new byte[1024 * 128];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            algorithm.AppendData(buffer, 0, read);
        }

        var hash = algorithm.GetHashAndReset();
        var actual = useSha512 ? Convert.ToBase64String(hash) : Convert.ToHexString(hash);
        if (!string.Equals(actual, descriptor.Checksum.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Plugin EP 包校验失败，下载内容可能已损坏或被篡改。");
        }
    }

    private static void ExtractPackage(OnnxRuntimePluginDescriptor descriptor, string archivePath,
        string destination)
    {
        const long maxExtractedBytes = 2L * 1024 * 1024 * 1024;
        using var archive = ZipFile.OpenRead(archivePath);
        var destinationRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        long extractedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (descriptor.Source == OnnxRuntimePluginSourceKind.NuGet)
            {
                const string nativePrefix = "runtimes/win-x64/native/";
                if (entry.FullName.StartsWith(nativePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = entry.FullName[nativePrefix.Length..]
                        .Replace('/', Path.DirectorySeparatorChar);
                }
                else if (string.Equals(entry.FullName, "README.md", StringComparison.OrdinalIgnoreCase) ||
                         Path.GetFileName(entry.FullName).Contains("license", StringComparison.OrdinalIgnoreCase) ||
                         Path.GetFileName(entry.FullName).Contains("notice", StringComparison.OrdinalIgnoreCase) ||
                         entry.FullName.Contains("third-party", StringComparison.OrdinalIgnoreCase) ||
                         entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = Path.GetFileName(entry.FullName);
                }
                else
                {
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(relativePath) ||
                relativePath.EndsWith(Path.DirectorySeparatorChar))
            {
                continue;
            }

            extractedBytes = checked(extractedBytes + entry.Length);
            if (extractedBytes > maxExtractedBytes)
            {
                throw new InvalidDataException("Plugin EP 解压后超过 2 GB 安全限制。");
            }

            var targetPath = Path.GetFullPath(Path.Combine(destination, relativePath));
            if (!targetPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Plugin EP 包包含不安全的文件路径。");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, true);
        }
    }

    private bool TryPrepareStorage(out string error)
    {
        try
        {
            Directory.CreateDirectory(StorageRoot);
            Directory.CreateDirectory(_packagesRoot);
            Directory.CreateDirectory(_downloadsRoot);
            var probePath = Path.Combine(StorageRoot, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probePath, "ok", new UTF8Encoding(false));
            File.Delete(probePath);
            error = "";
            return true;
        }
        catch (Exception exception)
        {
            error = $"ORT 目录不可写：{StorageRoot}。{exception.Message}";
            _logger.LogError(exception, "[ONNX] {Error}", error);
            return false;
        }
    }

    private PluginStoreState LoadState()
    {
        try
        {
            if (!File.Exists(_statePath))
            {
                return new PluginStoreState();
            }

            var state = JsonSerializer.Deserialize<PluginStoreState>(File.ReadAllText(_statePath), JsonOptions)
                        ?? new PluginStoreState();
            state.Plugins = new Dictionary<string, PluginInstallState>(state.Plugins ?? [],
                StringComparer.OrdinalIgnoreCase);
            state.PendingDeleteDirectories ??= [];
            return state;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "[ONNX] 读取 Plugin EP 状态失败，使用空状态");
            return new PluginStoreState();
        }
    }

    private IReadOnlyList<OnnxRuntimePluginDescriptor> LoadCachedCatalog()
    {
        try
        {
            if (!File.Exists(_catalogCachePath))
            {
                return [];
            }

            return JsonSerializer.Deserialize<OnnxRuntimePluginCatalog>(
                       File.ReadAllText(_catalogCachePath), JsonOptions)?.Packages ?? [];
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "[ONNX] 读取 Plugin EP 清单缓存失败");
            return [];
        }
    }

    private void SaveState()
    {
        if (!IsStorageWritable)
        {
            return;
        }

        var tempPath = _statePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(_state, JsonOptions), new UTF8Encoding(false));
        File.Move(tempPath, _statePath, true);
    }

    private static void WriteJsonAtomically<T>(string path, T value)
    {
        var tempPath = path + ".tmp";
        var json = JsonSerializer.Serialize(value, JsonOptions);
        File.WriteAllText(tempPath, json, new UTF8Encoding(false));
        File.Move(tempPath, path, true);
    }

    private void TryCleanupPendingDirectories()
    {
        if (!IsStorageWritable || _state.PendingDeleteDirectories.Count == 0)
        {
            return;
        }

        try
        {
            using (AcquireStoreMutex())
            {
                lock (_stateSyncRoot)
                {
                    _state = LoadState();
                    foreach (var directory in _state.PendingDeleteDirectories.ToArray())
                    {
                        try
                        {
                            if (IsPathUnderStorageRoot(directory) && Directory.Exists(directory))
                            {
                                Directory.Delete(directory, true);
                            }

                            _state.PendingDeleteDirectories.Remove(directory);
                        }
                        catch (IOException)
                        {
                            // 其他 BetterGI 实例仍可能加载这个目录，下次启动继续处理。
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // 目录权限可能稍后恢复，下次启动继续处理。
                        }
                    }

                    SaveState();
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "[ONNX] 清理待删除 Plugin EP 目录失败");
        }
    }

    private PluginInstallState GetOrCreateInstallState(string id)
    {
        if (!_state.Plugins.TryGetValue(id, out var installState))
        {
            installState = new PluginInstallState();
            _state.Plugins[id] = installState;
        }

        return installState;
    }

    private void AddPendingDelete(string directory)
    {
        if (!_state.PendingDeleteDirectories.Contains(directory, StringComparer.OrdinalIgnoreCase))
        {
            _state.PendingDeleteDirectories.Add(directory);
        }
    }

    private IReadOnlyList<OnnxRuntimePluginInfo> BuildPackageInfos(
        IReadOnlyList<OnnxRuntimePluginDescriptor> descriptors)
    {
        return descriptors.Select(descriptor =>
        {
            _state.Plugins.TryGetValue(descriptor.Id, out var installState);
            var packageDescriptor = IsTrustedInstalledDescriptor(descriptor)
                ? descriptor
                : installState?.PendingDescriptor is not null &&
                  IsTrustedInstalledDescriptor(installState.PendingDescriptor)
                    ? installState.PendingDescriptor
                    : installState?.ActiveDescriptor is not null &&
                      IsTrustedInstalledDescriptor(installState.ActiveDescriptor)
                        ? installState.ActiveDescriptor
                        : installState?.Descriptor is not null && IsTrustedInstalledDescriptor(installState.Descriptor)
                            ? installState.Descriptor
                    : descriptor;
            var installedVersion = installState?.PendingVersion;
            if (string.IsNullOrWhiteSpace(installedVersion))
            {
                installedVersion = installState?.ActiveVersion ?? "";
            }

            var hasRecordedVersion = !string.IsNullOrWhiteSpace(installedVersion);
            var versionPathSafe = hasRecordedVersion && IsSafePathSegment(installedVersion!);
            var installedDirectory = versionPathSafe
                ? GetVersionDirectory(descriptor.Id, installedVersion!)
                : "";
            var installed = versionPathSafe &&
                            FindEntryLibrary(installedDirectory, packageDescriptor.EntryLibrary) is not null;
            var broken = hasRecordedVersion && !installed;
            var pending = installed && !string.IsNullOrWhiteSpace(installState?.PendingVersion);
            var hasUpdate = installed && packageDescriptor.IsPublished &&
                            !string.Equals(installedVersion, packageDescriptor.Version,
                                StringComparison.OrdinalIgnoreCase);
            var compatibility = OnnxRuntimeDependencyChecker.Check(
                packageDescriptor, _configService.Get().HardwareAccelerationConfig);
            var catalogReady = IsTrustedInstalledDescriptor(packageDescriptor);
            var incompatibleOrt = !compatibility.IsCompatible &&
                                  compatibility.Message.StartsWith("需要 ORT", StringComparison.Ordinal);
            var status = !packageDescriptor.IsPublished && !installed
                ? OnnxRuntimePluginStatusKind.NotPublished
                : broken
                    ? OnnxRuntimePluginStatusKind.Broken
                    : !catalogReady
                        ? OnnxRuntimePluginStatusKind.Incompatible
                    : !compatibility.IsCompatible
                        ? incompatibleOrt
                            ? OnnxRuntimePluginStatusKind.Incompatible
                            : OnnxRuntimePluginStatusKind.MissingDependency
                        : pending
                            ? OnnxRuntimePluginStatusKind.PendingRestart
                            : hasUpdate
                                ? OnnxRuntimePluginStatusKind.UpdateAvailable
                                : installed
                                    ? OnnxRuntimePluginStatusKind.Installed
                                    : OnnxRuntimePluginStatusKind.NotInstalled;
            var message = status switch
            {
                OnnxRuntimePluginStatusKind.NotPublished => "项目下载源尚未发布此包",
                OnnxRuntimePluginStatusKind.Broken => "已安装的入口 DLL 缺失或目录已损坏",
                OnnxRuntimePluginStatusKind.Incompatible => catalogReady
                    ? compatibility.Message
                    : "尚未取得有效的下载地址或包校验信息",
                OnnxRuntimePluginStatusKind.MissingDependency => compatibility.Message,
                OnnxRuntimePluginStatusKind.PendingRestart => "已安装，重启后启用",
                OnnxRuntimePluginStatusKind.UpdateAvailable => "发现可用更新",
                OnnxRuntimePluginStatusKind.Installed => "已安装",
                _ => "未安装"
            };
            if (status == OnnxRuntimePluginStatusKind.PendingRestart &&
                installState?.PendingInstalledManually == true)
            {
                message = "已检测到手动安装，重启后启用";
            }
            else if (status == OnnxRuntimePluginStatusKind.Installed &&
                     installState?.ActiveInstalledManually == true)
            {
                message = "已手动安装";
            }
            if (packageDescriptor.Provider == InferenceDeviceType.OpenVino &&
                !string.IsNullOrWhiteSpace(descriptor.SourceLatestVersion) &&
                !string.Equals(descriptor.SourceLatestVersion, packageDescriptor.Version,
                    StringComparison.OrdinalIgnoreCase))
            {
                message += $"；官方已有 {descriptor.SourceLatestVersion}，需要应用更新后适配";
            }

            return new OnnxRuntimePluginInfo
            {
                Descriptor = packageDescriptor,
                InstalledVersion = installedVersion ?? "",
                Status = status,
                StatusMessage = message,
                CanDownload = IsStorageWritable && packageDescriptor.IsPublished && catalogReady && !incompatibleOrt,
                CanUninstall = IsStorageWritable && hasRecordedVersion
            };
        }).ToArray();
    }

    private static IReadOnlyList<OnnxRuntimePluginDescriptor> CreateBuiltInCatalog()
    {
        const string openVinoVersion = "1.7.0";
        var openVinoUrl =
            $"https://api.nuget.org/v3-flatcontainer/{OpenVinoPackageId}/{openVinoVersion}/{OpenVinoPackageId}.{openVinoVersion}.nupkg";
        return
        [
            new OnnxRuntimePluginDescriptor
            {
                Id = "openvino",
                DisplayName = "OpenVINO Plugin EP",
                Version = openVinoVersion,
                SourceLatestVersion = openVinoVersion,
                Provider = InferenceDeviceType.OpenVino,
                MinimumOrtVersion = "1.23.0",
                EntryLibrary = "onnxruntime_providers_openvino_plugin.dll",
                EpName = "OpenVINOExecutionProvider",
                DownloadUrl = openVinoUrl,
                ChecksumAlgorithm = "SHA512",
                // 来自 NuGet.org 1.7.0 Catalog Entry 的 packageHash；固定兼容版本可离线验证元数据。
                Checksum = "TOFTZAucE682gE3yeVRzjXSKR3VZBOZEufQ7ZQt0zP34lb/MOdQqIosUpoBI6R4w7Jx3GcxAB78THyjoQYt+tQ==",
                DownloadSize = 117_914_126,
                Source = OnnxRuntimePluginSourceKind.NuGet,
                ReleasePageUrl =
                    $"https://www.nuget.org/packages/Intel.ML.OnnxRuntime.EP.OpenVINO/{openVinoVersion}",
                Description = "支持 Intel CPU、GPU 和 NPU，运行库已包含在官方 NuGet 包中。"
            },
            CreateCudaDescriptor(12),
            CreateCudaDescriptor(13)
        ];
    }

    private static OnnxRuntimePluginDescriptor CreateCudaDescriptor(int major)
    {
        const string version = "0.1.0";
        var fileName = $"cuda_ep_cuda{major}_{version}_win-x64.zip";
        return new OnnxRuntimePluginDescriptor
        {
            Id = $"cuda{major}",
            DisplayName = $"CUDA {major} Plugin EP",
            Version = version,
            SourceLatestVersion = version,
            Provider = InferenceDeviceType.Cuda,
            CudaMajor = major,
            MinimumOrtVersion = "1.24.4",
            EntryLibrary = "onnxruntime_providers_cuda.dll",
            EpName = "CUDAExecutionProvider",
            DownloadUrl =
                $"https://github.com/microsoft/onnxruntime/releases/download/plugin-ep-cuda/v{version}/{fileName}",
            ReleasePageUrl = CudaReleasePageUrl,
            ChecksumAlgorithm = "SHA256",
            Checksum = major == 12
                ? "A9ABFCC11692EE886289C99D3CF95373A36595F55E72B4745A1742D1BF2623F5"
                : "CA2F3BD52539EB9F0E02A9B74EE9980AFAADA7C545D813D784ABF6621C4A6953",
            DownloadSize = major == 12 ? 191_925_307 : 132_565_271,
            Source = OnnxRuntimePluginSourceKind.GitHubRelease,
            IsPublished = true,
            Description = $"微软官方 CUDA Plugin EP 0.1.0；需要系统已安装 CUDA {major} 和 cuDNN 9。"
        };
    }

    private static IReadOnlyList<OnnxRuntimePluginDescriptor> MergeCatalog(
        IReadOnlyList<OnnxRuntimePluginDescriptor> builtIn,
        IReadOnlyList<OnnxRuntimePluginDescriptor> remote)
    {
        var result = builtIn.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in remote)
        {
            if (descriptor.Id.Equals("openvino", StringComparison.OrdinalIgnoreCase) &&
                result.TryGetValue("openvino", out var openVino) &&
                descriptor.Version.Equals(openVino.Version, StringComparison.OrdinalIgnoreCase) &&
                IsValidSha512(descriptor.Checksum))
            {
                openVino.Checksum = descriptor.Checksum;
                openVino.SourceLatestVersion = string.IsNullOrWhiteSpace(descriptor.SourceLatestVersion)
                    ? openVino.SourceLatestVersion
                    : descriptor.SourceLatestVersion;
                continue;
            }

            if (IsTrustedCudaDescriptor(descriptor) &&
                descriptor.Source == OnnxRuntimePluginSourceKind.GitHubRelease)
            {
                result[descriptor.Id] = descriptor;
            }
        }

        return result.Values.OrderBy(item => item.Provider).ThenBy(item => item.CudaMajor).ToArray();
    }

    private static void ValidateDescriptorForInstall(OnnxRuntimePluginDescriptor descriptor)
    {
        if (!IsTrustedInstalledDescriptor(descriptor))
        {
            throw new InvalidDataException("Plugin EP 包清单包含不受信任的路径、版本、来源或校验信息。");
        }
    }

    private static bool IsTrustedInstalledDescriptor(OnnxRuntimePluginDescriptor descriptor)
    {
        if (!IsSafePathSegment(descriptor.Id) || !IsSafePathSegment(descriptor.Version) ||
            descriptor.Rid != "win-x64" || !IsHttpsUrl(descriptor.DownloadUrl))
        {
            return false;
        }

        return descriptor.Provider switch
        {
            InferenceDeviceType.OpenVino =>
                descriptor.Id.Equals("openvino", StringComparison.OrdinalIgnoreCase) &&
                descriptor.Version == "1.7.0" &&
                descriptor.EntryLibrary.Equals("onnxruntime_providers_openvino_plugin.dll",
                    StringComparison.OrdinalIgnoreCase) &&
                descriptor.EpName.Equals("OpenVINOExecutionProvider", StringComparison.OrdinalIgnoreCase) &&
                descriptor.Source == OnnxRuntimePluginSourceKind.NuGet &&
                IsValidSha512(descriptor.Checksum),
            InferenceDeviceType.Cuda => IsTrustedCudaDescriptor(descriptor),
            _ => false
        };
    }

    private static bool IsTrustedCudaDescriptor(OnnxRuntimePluginDescriptor descriptor)
    {
        return descriptor.Provider == InferenceDeviceType.Cuda &&
               descriptor.CudaMajor is 12 or 13 &&
               descriptor.Id.Equals($"cuda{descriptor.CudaMajor}", StringComparison.OrdinalIgnoreCase) &&
               IsSafePathSegment(descriptor.Version) &&
               descriptor.Rid == "win-x64" &&
               descriptor.EntryLibrary.Equals("onnxruntime_providers_cuda.dll",
                   StringComparison.OrdinalIgnoreCase) &&
               descriptor.EpName.Equals("CUDAExecutionProvider", StringComparison.OrdinalIgnoreCase) &&
               descriptor.Source == OnnxRuntimePluginSourceKind.GitHubRelease &&
               descriptor.Version == "0.1.0" &&
               descriptor.ChecksumAlgorithm.Equals("SHA256", StringComparison.OrdinalIgnoreCase) &&
               descriptor.Checksum.Equals(descriptor.CudaMajor == 12
                       ? "A9ABFCC11692EE886289C99D3CF95373A36595F55E72B4745A1742D1BF2623F5"
                       : "CA2F3BD52539EB9F0E02A9B74EE9980AFAADA7C545D813D784ABF6621C4A6953",
                   StringComparison.OrdinalIgnoreCase) &&
               Uri.TryCreate(descriptor.DownloadUrl, UriKind.Absolute, out var downloadUri) &&
               downloadUri.Scheme == Uri.UriSchemeHttps &&
               downloadUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
               downloadUri.AbsolutePath.StartsWith(
                   "/microsoft/onnxruntime/releases/download/plugin-ep-cuda/v0.1.0/",
                   StringComparison.OrdinalIgnoreCase) &&
               (string.IsNullOrWhiteSpace(descriptor.FallbackDownloadUrl) ||
                 IsHttpsUrl(descriptor.FallbackDownloadUrl));
    }

    private static bool IsSafePathSegment(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_');
    }

    private static bool IsHttpsUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    }

    private static bool IsOfficialNuGetApiUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttps &&
               uri.Host.Equals("api.nuget.org", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidSha512(string value)
    {
        try
        {
            return Convert.FromBase64String(value).Length == 64;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static Version ParseVersion(string value)
    {
        return Version.TryParse(value, out var version) ? version : new Version();
    }

    private string GetVersionDirectory(string pluginId, string version)
    {
        return Path.Combine(GetPluginDirectory(pluginId), version, "win-x64");
    }

    private static string? FindEntryLibrary(string directory, string entryLibrary)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        return Directory.EnumerateFiles(directory, entryLibrary, SearchOption.AllDirectories).FirstOrDefault();
    }

    private bool IsPathUnderStorageRoot(string path)
    {
        var root = Path.GetFullPath(StorageRoot) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(path) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private IDisposable AcquireStoreMutex()
    {
        var mutex = new Mutex(false, "Local\\BetterGI.OrtPluginStore");
        try
        {
            try
            {
                if (!mutex.WaitOne(TimeSpan.FromSeconds(15)))
                {
                    throw new TimeoutException("等待其他 BetterGI 实例完成 ORT 包操作超时。");
                }
            }
            catch (AbandonedMutexException)
            {
                // 上一个进程异常退出，当前进程已经取得互斥锁，可以继续恢复状态。
            }

            return new MutexLease(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            UseCookies = false,
            UseDefaultCredentials = false
        }) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BetterGI-OrtPlugins", "1.0"));
        return client;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 临时文件清理失败不应覆盖原始下载异常。
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // 临时目录清理失败不应覆盖原始安装异常。
        }
    }

    private sealed class MutexLease(Mutex mutex) : IDisposable
    {
        public void Dispose()
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}

internal sealed class PluginStoreState
{
    public Dictionary<string, PluginInstallState> Plugins { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<string> PendingDeleteDirectories { get; set; } = [];
}

internal sealed class PluginInstallState
{
    public OnnxRuntimePluginDescriptor? Descriptor { get; set; }
    public OnnxRuntimePluginDescriptor? ActiveDescriptor { get; set; }
    public OnnxRuntimePluginDescriptor? PendingDescriptor { get; set; }
    public string ActiveVersion { get; set; } = "";
    public string PendingVersion { get; set; } = "";
    public bool ActiveInstalledManually { get; set; }
    public bool PendingInstalledManually { get; set; }
}
