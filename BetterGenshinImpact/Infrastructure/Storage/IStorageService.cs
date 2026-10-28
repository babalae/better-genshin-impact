using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Infrastructure.Storage;

/// <summary>
/// 统计和清理存储区域，供存储空间窗口和自动维护使用
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// 汇总所有 <see cref="IStorageAreaProvider"/> 登记的区域
    /// </summary>
    IReadOnlyList<StorageArea> GetAreas();

    /// <summary>
    /// 统计区域占用的字节数，在后台线程执行
    /// </summary>
    Task<long> MeasureAsync(StorageArea area, CancellationToken ct);

    /// <summary>
    /// 按区域的清理时机立即清理，或登记到下次启动清理
    /// </summary>
    /// <exception cref="System.InvalidOperationException">区域当前不能清理，异常信息为原因</exception>
    Task<StorageCleanupResult> CleanupAsync(StorageArea area, CancellationToken ct);
}
