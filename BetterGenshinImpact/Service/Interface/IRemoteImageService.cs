using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using BetterGenshinImpact.Infrastructure.Cache;

namespace BetterGenshinImpact.Service.Interface;

/// <summary>
/// 远程图片：下载经文件缓存落盘，解码后冻结，并放入有数量上限的内存缓存（见设计文档 5.4 节）
/// </summary>
public interface IRemoteImageService
{
    /// <summary>
    /// 内存命中时同步返回，供列表滚动和画布重绘时立即显示
    /// </summary>
    bool TryGetCached(CachePartition partition, string url, out ImageSource? image);

    /// <summary>
    /// 获取解码后的图片，失败返回 null。同一地址的并发请求只下载、解码一次
    /// </summary>
    Task<ImageSource?> GetAsync(CachePartition partition, string url, CancellationToken ct = default);

    /// <summary>
    /// 获取图片原始数据（经文件缓存），供 GIF 等需要自行解码的控件使用。下载失败时抛出异常，便于界面说明原因
    /// </summary>
    Task<byte[]?> GetBytesAsync(CachePartition partition, string url, CancellationToken ct = default);
}
