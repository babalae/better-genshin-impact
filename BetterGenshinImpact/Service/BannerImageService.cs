using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers.Http;
using BetterGenshinImpact.Service.Interface;
using SixLabors.ImageSharp;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service;

public sealed class BannerImageService : IBannerImageService
{
    private const int MaxDownloadBytes = 20 * 1024 * 1024;
    private const long MaxPixelCount = 40_000_000;
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(30);

    // 创建HTTPClient
    private readonly HttpClient _httpClient = HttpClientFactory.GetClient(
        "banner-image",
        CreateHttpClient);
    private readonly object _fileCommitLock = new();
    private long _latestOperationId;

    public string NetworkImagePath { get; } = Global.Absolute("User/Images/custom_banner_url.jpg");

    private string UrlConfigPath { get; } = Global.Absolute("User/Images/custom_banner_url.ini");

    public string? ReadConfiguredUrl()
    {
        if (!File.Exists(UrlConfigPath))
        {
            return null;
        }

        var url = File.ReadAllText(UrlConfigPath).Trim();
        return string.IsNullOrWhiteSpace(url) ? null : url;
    }

    public void SaveConfiguredUrl(string url)
    {
        var directory = Path.GetDirectoryName(UrlConfigPath)
                        ?? throw new InvalidOperationException("无法确定网络背景配置目录。");
        Directory.CreateDirectory(directory);

        var tempPath = $"{UrlConfigPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, url, new UTF8Encoding(false));
            lock (_fileCommitLock)
            {
                File.Move(tempPath, UrlConfigPath, true);
            }
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    public async Task<bool> DownloadAndSaveAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("请输入有效的 HTTP/HTTPS 图片地址。", nameof(url));
        }

        var operationId = Interlocked.Increment(ref _latestOperationId);
        var directory = Path.GetDirectoryName(NetworkImagePath)
                        ?? throw new InvalidOperationException("无法确定网络背景图片目录。");
        Directory.CreateDirectory(directory);
        var tempPath = $"{NetworkImagePath}.{Guid.NewGuid():N}.tmp";

        using var timeoutCancellationTokenSource = new CancellationTokenSource(DownloadTimeout);
        using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellationTokenSource.Token);
        var linkedCancellationToken = linkedCancellationTokenSource.Token;

        try
        {
            // 下载图片
            using var response = await _httpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                linkedCancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength > MaxDownloadBytes)
            {
                throw new InvalidDataException($"图片大小超过 {MaxDownloadBytes / 1024 / 1024} MB 限制。");
            }

            await using (var responseStream = await response.Content
                             .ReadAsStreamAsync(linkedCancellationToken)
                             .ConfigureAwait(false))
            await using (var fileStream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                long totalBytes = 0;
                int read;
                while ((read = await responseStream
                           .ReadAsync(buffer.AsMemory(), linkedCancellationToken)
                           .ConfigureAwait(false)) > 0)
                {
                    totalBytes += read;
                    if (totalBytes > MaxDownloadBytes)
                    {
                        throw new InvalidDataException($"图片大小超过 {MaxDownloadBytes / 1024 / 1024} MB 限制。");
                    }

                    await fileStream
                        .WriteAsync(buffer.AsMemory(0, read), linkedCancellationToken)
                        .ConfigureAwait(false);
                }

                if (totalBytes == 0)
                {
                    throw new InvalidDataException("下载的图片内容为空。");
                }
            }

            ValidateImage(tempPath);
            linkedCancellationToken.ThrowIfCancellationRequested();

            lock (_fileCommitLock)
            {
                if (operationId != Volatile.Read(ref _latestOperationId))
                {
                    return false;
                }

                linkedCancellationToken.ThrowIfCancellationRequested();
                // 保存图片到本地
                File.Move(tempPath, NetworkImagePath, true);
            }

            return true;
        }
        catch (OperationCanceledException ex) when (
            timeoutCancellationTokenSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"图片下载超时（{DownloadTimeout.TotalSeconds:0} 秒）。", ex);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    public void InvalidatePendingDownloads()
    {
        Interlocked.Increment(ref _latestOperationId);
    }

    public void ResetNetworkImage()
    {
        InvalidatePendingDownloads();
        lock (_fileCommitLock)
        {
            if (File.Exists(UrlConfigPath))
            {
                File.Delete(UrlConfigPath);
            }

            if (File.Exists(NetworkImagePath))
            {
                File.Delete(NetworkImagePath);
            }
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
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BetterGI-Banner", "1.0"));
        return client;
    }

    private static void ValidateImage(string path)
    {
        try
        {
            var imageInfo = Image.Identify(path)
                            ?? throw new InvalidDataException("下载内容不是有效的图片。");
            var pixelCount = checked((long)imageInfo.Width * imageInfo.Height);
            if (pixelCount > MaxPixelCount)
            {
                throw new InvalidDataException($"图片像素数量超过 {MaxPixelCount:N0} 限制。");
            }
        }
        catch (UnknownImageFormatException ex)
        {
            throw new InvalidDataException("下载内容不是支持的图片格式。", ex);
        }
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
        catch (IOException)
        {
            // 临时文件清理失败不应覆盖原始下载异常。
        }
        catch (UnauthorizedAccessException)
        {
            // 临时文件清理失败不应覆盖原始下载异常。
        }
    }
}
