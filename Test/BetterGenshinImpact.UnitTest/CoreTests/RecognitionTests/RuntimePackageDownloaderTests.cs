using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

public sealed class RuntimePackageDownloaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "bettergi-download-test-" + Guid.NewGuid().ToString("N"));

    public RuntimePackageDownloaderTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Download_ResumesPartialFileAndVerifiesHash()
    {
        var data = Encoding.UTF8.GetBytes("complete package");
        var package = Package(data);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(package.Url.AbsoluteUri)));
        await File.WriteAllBytesAsync(Path.Combine(_root, key + ".zip.part"), data[..4]);
        using var client = new HttpClient(new Handler(request =>
        {
            Assert.Equal(4, request.Headers.Range!.Ranges.Single().From);
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(data[4..])
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(4, data.Length - 1, data.Length);
            return response;
        }));

        var path = await new RuntimePackageDownloader(client)
            .DownloadAsync(package, _root, null, CancellationToken.None);

        Assert.Equal(data, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task UnsafeArchiveEntry_IsRejected()
    {
        var archive = CreateZip(("../outside.dll", "bad"));
        var destination = Directory.CreateDirectory(Path.Combine(_root, "stage")).FullName;

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            RuntimePackageDownloader.ExtractAsync(archive, destination, _ => true, CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(_root, "outside.dll")));
    }

    [Fact]
    public async Task Install_ReplacesSelectedRuntimeWithoutChangingOtherEngines()
    {
        var plugins = Path.Combine(_root, "Plugins", "OnnxRuntime");
        var cuda = Directory.CreateDirectory(Path.Combine(plugins, "CUDA")).FullName;
        var tensor = Directory.CreateDirectory(Path.Combine(plugins, "TensorRT")).FullName;
        await File.WriteAllTextAsync(Path.Combine(cuda, "onnxruntime.dll"), "old-cuda");
        await File.WriteAllTextAsync(Path.Combine(tensor, "onnxruntime.dll"), "trt");
        var entries = OnnxRuntimePackageCatalog.ProviderFiles(InferenceDeviceType.CUDA)
            .Select(file => ($"runtimes/win-x64/native/{file}", "new-cuda"))
            .ToArray();
        var archive = CreateZip(entries);
        var bytes = await File.ReadAllBytesAsync(archive);
        using var client = new HttpClient(new Handler(_ => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        }));
        using var installer = new OnnxRuntimeInstaller(_root, client);

        await installer.InstallAsync(InferenceDeviceType.CUDA,
            [Package(bytes) with { NugetId = "Microsoft.ML.OnnxRuntime.Gpu.Windows" }], null,
            CancellationToken.None);

        var installed = installer.Inspect(InferenceDeviceType.CUDA);
        Assert.Equal(cuda, installed.Directory);
        Assert.Equal("new-cuda", await File.ReadAllTextAsync(Path.Combine(installed.Directory, "onnxruntime.dll")));
        Assert.Equal("trt", await File.ReadAllTextAsync(Path.Combine(tensor, "onnxruntime.dll")));
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

    private static RuntimeDownloadPackage Package(byte[] data) => new("test",
        new("https://example.test/package.zip"), data.Length, data.Length * 4,
        Convert.ToBase64String(SHA512.HashData(data)), "SHA512");

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(respond(request));
    }

    public void Dispose()
    {
        var path = Path.GetFullPath(_root);
        if (!path.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(path).StartsWith("bettergi-download-test-"))
        {
            throw new InvalidOperationException();
        }

        Directory.Delete(path, true);
    }
}
