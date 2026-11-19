using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BetterGenshinImpact.GameTask.MapMask;

/// <summary>
/// 遮罩窗口本地点位键与 HoYoLAB 账号标记之间的转换。
/// 点位 ID 在各语言下相同，键中的语言只是沿用本地存储的格式。
/// </summary>
public static class MapMarkSync
{
    /// <summary>
    /// HoYoLAB 数据源的键前缀，与 <see cref="MapPointApiProvider.HoYoLab"/> 的名称一致
    /// </summary>
    private const string HoYoLabPrefix = nameof(MapPointApiProvider.HoYoLab) + ":";

    /// <summary>
    /// 从本地隐藏键中解析 HoYoLAB 点位 ID；其他数据源和格式错误的键忽略
    /// </summary>
    /// <param name="keys">本地保存的隐藏键，格式 HoYoLab:{lang}:{pointId}</param>
    /// <returns>点位 ID，保持原顺序</returns>
    public static IEnumerable<long> ParseLocalPointIds(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (!key.StartsWith(HoYoLabPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var parts = key.Split(':');
            if (parts.Length == 3 && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                yield return id;
            }
        }
    }

    /// <summary>
    /// 计算需要上传到账号的点位：去重、排除账号已标记的，并排序
    /// </summary>
    /// <param name="local">本地标记的点位 ID</param>
    /// <param name="account">账号已标记的点位 ID</param>
    /// <returns>待上传的点位 ID</returns>
    public static IReadOnlyList<long> PendingUpload(IEnumerable<long> local, IReadOnlySet<long> account)
    {
        return local.Distinct().Where(id => !account.Contains(id)).OrderBy(id => id).ToList();
    }

    /// <summary>
    /// 生成遮罩窗口使用的点位键
    /// </summary>
    /// <param name="lang">HoYoLAB 接口语言</param>
    /// <param name="pointId">点位 ID</param>
    /// <returns>HoYoLab:{lang}:{pointId}</returns>
    public static string ToKey(string lang, long pointId)
    {
        return $"{HoYoLabPrefix}{lang}:{pointId.ToString(CultureInfo.InvariantCulture)}";
    }
}
