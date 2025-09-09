using System;
using BetterGenshinImpact.GameTask;

namespace BetterGenshinImpact.Helpers;

public class TimeZoneHelper
{
    public static DateTimeOffset GetServerTimeNow() =>
        DateTimeOffset.UtcNow.ToOffset(TaskContext.Instance().Config.OtherConfig.ServerTimeZoneOffset);
}