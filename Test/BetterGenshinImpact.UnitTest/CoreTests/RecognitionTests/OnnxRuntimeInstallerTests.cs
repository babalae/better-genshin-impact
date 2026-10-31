using System.Net;
using System.Text;
using System.Xml.Linq;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

public sealed class OnnxRuntimeInstallerTests
{
    private static readonly string Hash = Convert.ToBase64String(new byte[64]);
    private const string CatalogUrl = "https://api.nuget.org/v3/catalog0/data/openvino.json";
    private static readonly XNamespace DataNamespace = "http://schemas.microsoft.com/ado/2007/08/dataservices";
    private static readonly XNamespace MetadataNamespace = "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata";

    [Fact]
    public async Task DownloadPlan_UsesCatalogWhenAvailable()
    {
        var requests = new List<string>();
        using var client = CreateClient(request =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            return request.RequestUri.AbsoluteUri == CatalogUrl
                ? Json(new JObject { ["packageSize"] = 1234, ["packageHashAlgorithm"] = "SHA512", ["packageHash"] = Hash })
                : Registration();
        });
        using var installer = new OnnxRuntimeInstaller(AppContext.BaseDirectory, client);

        var package = Assert.Single(await installer.GetDownloadPlanAsync(InferenceDeviceType.OpenVINO, false, CancellationToken.None));

        AssertPackage(package);
        Assert.Equal(2, requests.Count);
        Assert.DoesNotContain(requests, url => url.Contains("/api/v2/"));
    }

    [Theory]
    [InlineData(InferenceDeviceType.OpenVINO, "Intel.ML.OnnxRuntime.OpenVino", 1)]
    [InlineData(InferenceDeviceType.DirectML, "Microsoft.ML.OnnxRuntime.DirectML", 2)]
    [InlineData(InferenceDeviceType.CUDA, "Microsoft.ML.OnnxRuntime.Gpu.Windows", 1)]
    [InlineData(InferenceDeviceType.TensorRT, "Microsoft.ML.OnnxRuntime.Gpu.Windows", 1)]
    public async Task DownloadPlan_Catalog404FallsBackToV2(InferenceDeviceType engine, string id, int count)
    {
        var fallbackRequests = new List<string>();
        using var client = CreateClient(request =>
        {
            if (request.RequestUri!.AbsoluteUri == CatalogUrl) return new(HttpStatusCode.NotFound);
            if (request.RequestUri.Host != "www.nuget.org") return Registration();
            Assert.Equal("application/atom+xml", Assert.Single(request.Headers.Accept).MediaType);
            fallbackRequests.Add(request.RequestUri.AbsoluteUri);
            return V2Metadata();
        });
        using var installer = new OnnxRuntimeInstaller(AppContext.BaseDirectory, client);

        var packages = await installer.GetDownloadPlanAsync(engine, false, CancellationToken.None);

        Assert.Equal(count, packages.Count);
        AssertPackage(packages[0], id);
        Assert.Equal(count, fallbackRequests.Count);
        Assert.Contains($"Id='{id}',Version='{OnnxRuntimeLoader.RequiredVersion}'", fallbackRequests[0]);
        if (engine == InferenceDeviceType.DirectML)
            Assert.Contains("Id='Microsoft.AI.DirectML',Version='1.15.4'", fallbackRequests[1]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task DownloadPlan_OtherCatalogErrorsArePreserved(HttpStatusCode status)
    {
        using var client = CreateClient(request => request.RequestUri!.AbsoluteUri == CatalogUrl
            ? new(status) : request.RequestUri.Host == "www.nuget.org"
                ? throw new InvalidOperationException("不应回退到 V2") : Registration());
        using var installer = new OnnxRuntimeInstaller(AppContext.BaseDirectory, client);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            installer.GetDownloadPlanAsync(InferenceDeviceType.OpenVINO, false, CancellationToken.None));

        Assert.Equal(status, error.StatusCode);
    }

    [Theory]
    [InlineData("PackageSize", "0")]
    [InlineData("PackageSize", "9223372036854775807")]
    [InlineData("PackageHashAlgorithm", "SHA256")]
    [InlineData("PackageHash", null)]
    [InlineData("PackageHash", "not-base64")]
    [InlineData("PackageHash", "YWJj")]
    public async Task DownloadPlan_InvalidFallbackMetadataIsRejected(string field, string? value)
    {
        using var client = CreateClient(request =>
        {
            if (request.RequestUri!.AbsoluteUri == CatalogUrl) return new(HttpStatusCode.NotFound);
            if (request.RequestUri.Host != "www.nuget.org") return Registration();
            var metadata = ValidV2Metadata();
            var element = metadata.Element(MetadataNamespace + "properties")!.Element(DataNamespace + field)!;
            if (value == null) element.Remove();
            else element.Value = value;
            return Xml(metadata);
        });
        using var installer = new OnnxRuntimeInstaller(AppContext.BaseDirectory, client);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            installer.GetDownloadPlanAsync(InferenceDeviceType.OpenVINO, false, CancellationToken.None));
    }

    [Fact]
    public async Task DownloadPlan_CancellationDuringFallbackIsPreserved()
    {
        using var cancellation = new CancellationTokenSource();
        using var client = CreateClient(request =>
        {
            if (request.RequestUri!.AbsoluteUri == CatalogUrl) return new(HttpStatusCode.NotFound);
            if (request.RequestUri.Host != "www.nuget.org") return Registration();
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        using var installer = new OnnxRuntimeInstaller(AppContext.BaseDirectory, client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            installer.GetDownloadPlanAsync(InferenceDeviceType.OpenVINO, false, cancellation.Token));
    }

    private static void AssertPackage(RuntimeDownloadPackage package, string id = "Intel.ML.OnnxRuntime.OpenVino")
    {
        Assert.Equal(id, package.NugetId);
        Assert.Equal(1234, package.DownloadBytes);
        Assert.Equal(4936, package.InstalledBytes);
        Assert.Equal("SHA512", package.HashAlgorithm);
        Assert.Equal(Hash, package.Hash);
        Assert.Equal($"https://api.nuget.org/v3-flatcontainer/{id.ToLowerInvariant()}/{OnnxRuntimeLoader.RequiredVersion}/{id.ToLowerInvariant()}.{OnnxRuntimeLoader.RequiredVersion}.nupkg",
            package.Url.AbsoluteUri);
    }

    private static HttpResponseMessage Registration() => Json(new JObject { ["catalogEntry"] = CatalogUrl });
    private static HttpResponseMessage V2Metadata() => Xml(ValidV2Metadata());
    private static XElement ValidV2Metadata() => new(XName.Get("entry", "http://www.w3.org/2005/Atom"),
        new XAttribute(XNamespace.Xmlns + "m", MetadataNamespace), new XAttribute(XNamespace.Xmlns + "d", DataNamespace),
        new XElement(MetadataNamespace + "properties",
            new XElement(DataNamespace + "PackageSize", "1234"),
            new XElement(DataNamespace + "PackageHashAlgorithm", "SHA512"),
            new XElement(DataNamespace + "PackageHash", Hash)));
    private static HttpResponseMessage Xml(XElement value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value.ToString(), Encoding.UTF8, "application/atom+xml")
    };
    private static HttpResponseMessage Json(JObject value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value.ToString(), Encoding.UTF8, "application/json")
    };
    private static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler) => new(new Handler(handler));
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
