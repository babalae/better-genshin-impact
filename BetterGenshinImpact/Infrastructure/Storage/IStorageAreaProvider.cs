using System.Collections.Generic;

namespace BetterGenshinImpact.Infrastructure.Storage;

/// <summary>
/// 存储区域来源。各功能实现它来登记自己占用的磁盘区域，可以返回动态数量的区域（如每个云原神实例一项）
/// </summary>
public interface IStorageAreaProvider
{
    /// <summary>
    /// 当前的存储区域。每次打开存储空间窗口时重新获取
    /// </summary>
    IEnumerable<StorageArea> GetAreas();
}
