using System;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// Pulonia 统一使用的服务器时间：通用配置的时区偏移取自 <see cref="ServerTimeHelper"/>，账户的时区偏移取自账户的服务器时区；
/// 游戏日按服务器时间凌晨 4 点切换。路线 CD 的日、周边界使用这里的游戏日计算。
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
    /// 读取通用配置的服务器时区偏移，单位分钟。新运行入队时先按它填写请求，运行开始确定运行账户后改为账户的服务器时区；
    /// 不含游戏节点、没有运行账户的运行保留这个值。
    /// </summary>
    /// <returns>服务器相对 UTC 的分钟偏移。</returns>
    public static int GetUtcOffsetMinutes() => (int)Math.Round(GetUtcOffset().TotalMinutes);

    /// <summary>
    /// 账户服务器时区的分钟偏移：UtcPlus8 为 480，Europe 为 60，America 为 -300。路线 CD 的日切按运行账户（或界面所选账户）计算。
    /// </summary>
    /// <param name="serverTimeZone">账户的服务器时区。</param>
    /// <returns>服务器相对 UTC 的分钟偏移。</returns>
    /// <exception cref="FormatException">时区不是支持的取值。</exception>
    public static int GetUtcOffsetMinutes(PuloniaTaskServerTimeZone serverTimeZone)
        => (int)PuloniaTaskSchedule.GetUtcOffset(serverTimeZone).TotalMinutes;

    /// <summary>
    /// 把通用配置的服务器时区偏移映射为账户的服务器时区：+8 为 UTC+8（国服、亚服、港澳台），+1 为欧服，-5 为美服，
    /// 其他偏移按 UTC+8 处理。迁移旧账户和新增账户的默认值共用此映射。
    /// </summary>
    /// <param name="utcOffset">通用配置 <c>OtherConfig.ServerTimeZoneOffset</c> 的值。</param>
    /// <returns>账户的服务器时区。</returns>
    public static PuloniaTaskServerTimeZone ToServerTimeZone(TimeSpan utcOffset) => (int)Math.Round(utcOffset.TotalMinutes) switch
    {
        8 * 60 => PuloniaTaskServerTimeZone.UtcPlus8,
        1 * 60 => PuloniaTaskServerTimeZone.Europe,
        -5 * 60 => PuloniaTaskServerTimeZone.America,
        _ => PuloniaTaskServerTimeZone.UtcPlus8
    };

    /// <summary>
    /// 新增账户的默认服务器时区：按当前通用配置的服务器时区映射；配置尚未初始化时按 UTC+8。
    /// </summary>
    /// <returns>默认服务器时区。</returns>
    public static PuloniaTaskServerTimeZone GetDefaultServerTimeZone() => ToServerTimeZone(GetUtcOffset());

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
