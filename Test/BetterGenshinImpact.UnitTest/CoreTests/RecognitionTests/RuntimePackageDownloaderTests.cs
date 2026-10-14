using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;
using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

public sealed class RuntimePackageDownloaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bettergi-download-test-" + Guid.NewGuid().ToString("N"));
    public RuntimePackageDownloaderTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Zip64Archive_ValidatesAndExtracts()
    {
        var file = CreateZip(("bin/test.dll", "zip64"));
        var original = await File.ReadAllBytesAsync(file);
        var zip64 = ToZip64(original);
        File.WriteAllBytes(file, zip64);
        var destination = Directory.CreateDirectory(Path.Combine(_root, "zip64-output")).FullName;
        await RuntimePackageDownloader.ExtractAsync(file, destination, _ => true, CancellationToken.None);
        Assert.Equal("zip64", await File.ReadAllTextAsync(Path.Combine(destination, "test.dll")));
    }

    [Fact]
    public async Task TensorRtPlan_DoesNotInstallCudaProviderOrMathLibraries()
    {
        using var client = new HttpClient(new Handler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"cuda_cudart":{"version":"12.8.57","windows-x86_64":{"relative_path":"cuda_cudart/windows-x86_64/test.zip","sha256":"0000000000000000000000000000000000000000000000000000000000000000","size":"32"}}}
                """)
        }));
        using var installer = new OnnxRuntimeInstaller(_root, client);
        var plan = await installer.GetDownloadPlanAsync(InferenceDeviceType.TensorRT, true, CancellationToken.None);
        Assert.Equal(2, plan.Count);
        Assert.Contains(plan, package => package.Name.StartsWith("cuda_cudart"));
        Assert.Contains(plan, package => package.Name.StartsWith("TensorRT"));
        Assert.DoesNotContain("onnxruntime_providers_cuda.dll", OnnxRuntimePackageCatalog.ProviderFiles(InferenceDeviceType.TensorRT));
        Assert.DoesNotContain(OnnxRuntimePackageCatalog.DependencyFiles(InferenceDeviceType.TensorRT), file => file.StartsWith("cublas") || file.StartsWith("cudnn") || file.StartsWith("nvrtc"));
    }

    [Fact]
    public async Task CompleteCorruptPartial_IsRestartedInsteadOfRequestingPastEnd()
    {
        var data = Encoding.UTF8.GetBytes("valid archive");
        var package = Package(data);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(package.Url.AbsoluteUri)));
        await File.WriteAllBytesAsync(Path.Combine(_root, key + ".zip.part"), new byte[data.Length]);
        using var client = new HttpClient(new Handler(request =>
        {
            Assert.Null(request.Headers.Range);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
        }));
        var path = await new RuntimePackageDownloader(client).DownloadAsync(package, _root, null, CancellationToken.None);
        Assert.Equal(data, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task CanceledDownload_KeepsPartialAndDisposesInput()
    {
        using var cancellation = new CancellationTokenSource();
        var data = Encoding.UTF8.GetBytes("cancel and resume");
        var input = new InterruptedStream(data, cancellation);
        using var client = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StreamContent(input) }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RuntimePackageDownloader(client).DownloadAsync(Package(data), _root, null, cancellation.Token));
        Assert.True(input.WasDisposed);
        Assert.Equal(data[..4], await File.ReadAllBytesAsync(Directory.GetFiles(_root, "*.part").Single()));
    }

    [Fact]
    public async Task PartialDownload_IsResumedAndHashVerified()
    {
        var data = Encoding.UTF8.GetBytes("complete package");
        var package = Package(data);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(package.Url.AbsoluteUri)));
        await File.WriteAllBytesAsync(Path.Combine(_root, key + ".zip.part"), data[..4]);
        using var client = new HttpClient(new Handler(request =>
        {
            Assert.Equal(4, request.Headers.Range!.Ranges.Single().From);
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(data[4..]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(4, data.Length - 1, data.Length);
            return response;
        }));
        var path = await new RuntimePackageDownloader(client).DownloadAsync(package, _root, null, CancellationToken.None);
        Assert.Equal(data, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task HashFailure_DoesNotCommitArchive()
    {
        var expected = Encoding.UTF8.GetBytes("expected");
        using var client = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("corrupt!")) }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new RuntimePackageDownloader(client).DownloadAsync(Package(expected), _root, null, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task UnsafeArchiveEntry_IsRejected()
    {
        var archive = CreateZip(("../outside.dll", "bad"));
        var destination = Directory.CreateDirectory(Path.Combine(_root, "stage")).FullName;
        await Assert.ThrowsAsync<InvalidDataException>(() => RuntimePackageDownloader.ExtractAsync(archive, destination, _ => true, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(_root, "outside.dll")));
    }

    [Fact]
    public async Task ZipCrcMismatch_IsRejected()
    {
        var archive = CreateZip(("bin/test.dll", "content"));
        var bytes = await File.ReadAllBytesAsync(archive);
        var index = FindCentralDirectory(bytes);
        bytes[index + 16] ^= 1;
        await File.WriteAllBytesAsync(archive, bytes);
        var stage = Directory.CreateDirectory(Path.Combine(_root, "stage")).FullName;
        await Assert.ThrowsAsync<InvalidDataException>(() => RuntimePackageDownloader.ExtractAsync(archive, stage, _ => true, CancellationToken.None));
    }

    [Fact]
    public async Task Install_KeepsEnginesIndependentAndLeavesLoadedFilesUntouched()
    {
        var plugins = Path.Combine(_root, "Plugins", "OnnxRuntime");
        var cuda = Directory.CreateDirectory(Path.Combine(plugins, "CUDA")).FullName;
        var tensor = Directory.CreateDirectory(Path.Combine(plugins, "TensorRT")).FullName;
        await File.WriteAllTextAsync(Path.Combine(cuda, "onnxruntime.dll"), "old-cuda");
        await File.WriteAllTextAsync(Path.Combine(tensor, "onnxruntime.dll"), "trt");
        var entries = OnnxRuntimePackageCatalog.ProviderFiles(InferenceDeviceType.CUDA).Select(file => ($"runtimes/win-x64/native/{file}", "new-cuda")).ToArray();
        var archive = CreateZip(entries);
        var bytes = await File.ReadAllBytesAsync(archive);
        using var client = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        using var installer = new OnnxRuntimeInstaller(_root, client);
        await installer.InstallAsync(InferenceDeviceType.CUDA, [Package(bytes) with { NugetId = "Microsoft.ML.OnnxRuntime.Gpu.Windows" }], null, CancellationToken.None);
        var installed = installer.Inspect(InferenceDeviceType.CUDA);
        Assert.NotEqual(cuda, installed.Directory);
        Assert.Equal("new-cuda", await File.ReadAllTextAsync(Path.Combine(installed.Directory, "onnxruntime.dll")));
        Assert.Equal("old-cuda", await File.ReadAllTextAsync(Path.Combine(cuda, "onnxruntime.dll")));
        Assert.Equal("trt", await File.ReadAllTextAsync(Path.Combine(tensor, "onnxruntime.dll")));
        Assert.Equal(InferenceInstallationState.MissingDependencies, installed.State);
        Assert.False(Directory.Exists(Path.Combine(plugins, ".downloads")));
    }

    [Fact]
    public async Task Cleanup_DeletesInstalledLegacyArchiveAndPreservesPartialOrUninstalledPackages()
    {
        var plugins = Path.Combine(_root, "Plugins", "OnnxRuntime");
        var directory = Directory.CreateDirectory(Path.Combine(plugins, "OpenVINO")).FullName;
        var cache = Directory.CreateDirectory(Path.Combine(plugins, ".downloads")).FullName;
        var files = OnnxRuntimePackageCatalog.ProviderFiles(InferenceDeviceType.OpenVINO);
        foreach (var file in files) await File.WriteAllTextAsync(Path.Combine(directory, file), "installed");
        var archive = CreateZip(files.Select(file => ($"runtimes/win-x64/native/{file}", "installed")).ToArray());
        var cached = Path.Combine(cache, new string('A', 64) + ".zip");
        File.Move(archive, cached);
        var partial = Path.Combine(cache, new string('B', 64) + ".zip.part");
        await File.WriteAllTextAsync(partial, "incomplete");
        var other = Path.Combine(cache, new string('C', 64) + ".zip");
        File.Move(CreateZip(("runtimes/win-x64/native/onnxruntime.dll", "different-package")), other);
        using var installer = new OnnxRuntimeInstaller(_root);
        await installer.CleanupInstalledDownloadsAsync(CancellationToken.None);
        Assert.False(File.Exists(cached));
        Assert.True(File.Exists(partial));
        Assert.True(File.Exists(other));
        Assert.All(files, file => Assert.Equal("installed", File.ReadAllText(Path.Combine(directory, file))));
    }

    [Fact]
    public async Task FailedInstall_PreservesCompleteArchiveForRetry()
    {
        var archive = CreateZip(("runtimes/win-x64/native/onnxruntime.dll", "incomplete-package"));
        var bytes = await File.ReadAllBytesAsync(archive);
        using var client = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        using var installer = new OnnxRuntimeInstaller(_root, client);
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(InferenceDeviceType.CUDA,
            [Package(bytes) with { NugetId = "Microsoft.ML.OnnxRuntime.Gpu.Windows" }], null, CancellationToken.None));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(installer.PluginsDirectory, ".downloads"), "*.zip"));
    }

    private static byte[] ToZip64(byte[] original)
    {
        var end = original.Length - 22;
        var directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(end + 16));
        var directorySize = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(end + 12));
        var zip64 = new byte[original.Length + 76];
        original.AsSpan(0, end).CopyTo(zip64);
        var record = zip64.AsSpan(end, 56);
        BinaryPrimitives.WriteUInt32LittleEndian(record, 0x06064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(record[4..], 44);
        BinaryPrimitives.WriteUInt16LittleEndian(record[12..], 45);
        BinaryPrimitives.WriteUInt16LittleEndian(record[14..], 45);
        BinaryPrimitives.WriteUInt64LittleEndian(record[24..], 1);
        BinaryPrimitives.WriteUInt64LittleEndian(record[32..], 1);
        BinaryPrimitives.WriteUInt64LittleEndian(record[40..], directorySize);
        BinaryPrimitives.WriteUInt64LittleEndian(record[48..], directoryOffset);
        var locator = zip64.AsSpan(end + 56, 20);
        BinaryPrimitives.WriteUInt32LittleEndian(locator, 0x07064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(locator[8..], (ulong)end);
        BinaryPrimitives.WriteUInt32LittleEndian(locator[16..], 1);
        original.AsSpan(end).CopyTo(zip64.AsSpan(end + 76));
        var eocd = zip64.AsSpan(end + 76);
        BinaryPrimitives.WriteUInt16LittleEndian(eocd[8..], ushort.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(eocd[10..], ushort.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(eocd[12..], uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(eocd[16..], uint.MaxValue);
        return zip64;
    }

    private string CreateZip(params (string Name, string Content)[] files)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            using var writer = new StreamWriter(archive.CreateEntry(file.Name).Open(), new UTF8Encoding(false));
            writer.Write(file.Content);
        }
        return path;
    }
    private static int FindCentralDirectory(byte[] bytes)
    {
        for (var i = 0; i < bytes.Length - 4; i++)
            if (bytes[i] == 0x50 && bytes[i + 1] == 0x4b && bytes[i + 2] == 1 && bytes[i + 3] == 2) return i;
        throw new InvalidDataException();
    }
    private static RuntimeDownloadPackage Package(byte[] data) => new("test", new("https://example.test/package.zip"), data.Length, data.Length * 4,
        Convert.ToBase64String(SHA512.HashData(data)), "SHA512");
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
    private sealed class InterruptedStream(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        public bool WasDisposed { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (Position >= 4) { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); }
            return base.ReadAsync(buffer[..Math.Min(4, buffer.Length)], token);
        }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
    public void Dispose()
    {
        var path = Path.GetFullPath(_root);
        if (!path.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(path).StartsWith("bettergi-download-test-")) throw new InvalidOperationException();
        Directory.Delete(path, true);
    }
}
