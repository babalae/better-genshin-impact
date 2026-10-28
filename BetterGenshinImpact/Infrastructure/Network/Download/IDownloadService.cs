using System.Threading;

namespace BetterGenshinImpact.Infrastructure.Network.Download;

/// <summary>
/// 下载服务：断点续传、重试、校验、并发上限和落盘位置。
/// 文件大于约 1 MB，或需要进度、续传、校验时使用；图标等小文件由缓存层直接请求
/// </summary>
public interface IDownloadService
{
    /// <summary>
    /// 开始下载并立即返回任务。ct 取消时取消下载
    /// </summary>
    /// <exception cref="System.ArgumentException">地址不是 http/https，或目标路径不在程序目录内</exception>
    DownloadTask Start(DownloadRequest request, CancellationToken ct = default);

    /// <summary>
    /// 分类的下载目录。完成的文件和未完成的 .part 都在这里
    /// </summary>
    string GetDirectory(DownloadCategory category);
}
