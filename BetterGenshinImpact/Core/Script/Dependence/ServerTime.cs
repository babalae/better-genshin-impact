using System;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.Core.Script.Dependence;

public static class ServerTime
{
    /// <summary>
    /// Gets the server's timezone offset from UTC.
    /// </summary>
    /// <returns>
    /// The offset in milliseconds.
    /// This value can be directly used in JavaScript: `new Date(Date.now() + offset)`
    /// </returns>
    public static int GetServerTimeZoneOffset()
    {
        return (int)ServerTimeHelper.GetServerTimeNow().Offset.TotalMilliseconds;
    }
}