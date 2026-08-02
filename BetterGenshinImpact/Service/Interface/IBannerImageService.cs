using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service.Interface;

public interface IBannerImageService
{
    string NetworkImagePath { get; }

    string? ReadConfiguredUrl();

    void SaveConfiguredUrl(string url);

    Task<bool> DownloadAndSaveAsync(string url, CancellationToken cancellationToken = default);

    void InvalidatePendingDownloads();

    void ResetNetworkImage();
}
