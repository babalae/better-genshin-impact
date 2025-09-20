using System;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.Core.Script.Dependence;

public static class ServerTime
{
    /// <summary>
    /// 获取服务器时区偏移量
    /// </summary>
    /// <returns>
    /// 以毫秒为单位的偏移量
    /// 该值可直接在JavaScript中使用：`new Date(Date.now() + offset)`
    /// </returns>
    public static int GetServerTimeZoneOffset()
    {
        return (int)ServerTimeHelper.GetServerTimeOffset().TotalMilliseconds;
    }
}