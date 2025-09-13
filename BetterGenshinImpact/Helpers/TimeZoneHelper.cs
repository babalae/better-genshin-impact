using System;
using BetterGenshinImpact.GameTask;

namespace BetterGenshinImpact.Helpers;

public static class TimeZoneHelper
{
    public static DateTimeOffset GetServerTimeNow()
    {
        try
        {
            return DateTimeOffset.UtcNow.ToOffset(TaskContext.Instance().Config.OtherConfig.ServerTimeZoneOffset);
        }
        // throw new Exception("Config未初始化"); in TaskContext.cs
        catch (Exception)
        {
            // Assume Beijing timezone for core developers' testing 
            return DateTimeOffset.Now;
        }
    }
}