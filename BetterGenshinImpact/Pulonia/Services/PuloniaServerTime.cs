using System;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// Pulonia 统一使用的服务器时间：时区偏移取自应用配置（<see cref="ServerTimeHelper"/>），游戏日按服务器时间凌晨 4 点切换。
/// 路线 CD 的日、周边界使用这里的游戏日计算。
/// </summary>
public static class PuloniaServerTime
{
    /// <summary>
    /// 游戏日的切换时刻（服务器时间的小时）。
    /// </summary>
    public const int DayResetHour = 4;

    /// <summary>
    /// <see cref="ServerTimeHelper"/> 尚未初始化（单元测试或启动早期）时使用的偏移，与配置读取失败时的回退一致，按 UTC+8。
    /// </summary>
    private static readonly TimeSpan FallbackUtcOffset = TimeSpan.FromHours(8);

    /// <summary>
    /// 读取当前配置的服务器时区偏移。
    /// </summary>
    /// <returns>服务器相对 UTC 的偏移。</returns>
    public static TimeSpan GetUtcOffset()
    {
        try
        {
            return ServerTimeHelper.GetServerTimeOffset();
        }
        catch (InvalidOperationException)
        {
            // 应用启动时才初始化；未初始化时不阻止入队，按默认服务器时区处理。
            return FallbackUtcOffset;
        }
    }

    /// <summary>
    /// 读取当前配置的服务器时区偏移，单位分钟，写入新运行请求。
    /// </summary>
    /// <returns>服务器相对 UTC 的分钟偏移。</returns>
    public static int GetUtcOffsetMinutes() => (int)Math.Round(GetUtcOffset().TotalMinutes);

    /// <summary>
    /// 计算某一时刻所属的游戏日：换算到服务器时区后，重置时刻之前仍属于前一天。
    /// </summary>
    /// <param name="instant">任意时区的时刻。</param>
    /// <param name="utcOffset">服务器相对 UTC 的偏移。</param>
    /// <param name="resetHour">游戏日切换的小时，默认凌晨 4 点。</param>
    /// <returns>游戏日日期（不含时刻，Kind 为 Unspecified）。</returns>
    public static DateTime GetGameDate(DateTimeOffset instant, TimeSpan utcOffset, int resetHour = DayResetHour)
        => instant.ToOffset(utcOffset).AddHours(-resetHour).Date;
}
