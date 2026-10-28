using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Infrastructure.Cache;

/// <summary>
/// 文件缓存：内存加磁盘两级，适合可再生的图片和接口数据（见设计文档第 5 节）
/// </summary>
public interface IFileCache
{
    /// <summary>
    /// 读取缓存，未命中或过期时调用 factory 获取并写入。factory 返回 null 或空内容时不缓存。
    /// 同一条目的并发请求只调用一次 factory；factory 收到的是缓存服务自身的令牌，ct 只取消本次等待
    /// </summary>
    /// <param name="partition">缓存分区</param>
    /// <param name="key">条目标识，例如图片地址</param>
    /// <param name="factory">获取内容，例如下载</param>
    /// <param name="ct">取消等待</param>
    Task<byte[]?> GetOrAddBytesAsync(CachePartition partition, string key,
        Func<CancellationToken, Task<byte[]?>> factory, CancellationToken ct = default);

    /// <summary>
    /// 删除一条缓存
    /// </summary>
    void Remove(CachePartition partition, string key);

    /// <summary>
    /// 只保留给定的条目，删除分区内其他条目
    /// </summary>
    void RetainOnly(CachePartition partition, IReadOnlyCollection<string> keys);
}
