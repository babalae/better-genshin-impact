using System;

namespace BetterGenshinImpact.GameTask.UseRedeemCode;

public class GamePreviewLiveDateCalculator
{
    private static readonly DateTime StartDate = new DateTime(2025, 10, 10);
    private const int IntervalDays = 42;
    private const double ValidDays = 3.5;

    /// <summary>
    /// 计算当前日期是否是前瞻日期
    /// </summary>
    /// <returns>如果是前瞻日期，返回 true；否则返回 false。</returns>
    public static bool IsPreviewDate(DateTime date)
    {
        TimeSpan difference = date - StartDate;
        return difference.Days >= 0 && difference.Days % IntervalDays == 0;
    }
    
    public static void TestIsPreviewDate()
    {
         IsPreviewDate(new DateTime(2025, 11, 21));
    }
    
    public bool TestTodayIsPreviewDate()
    {
        return IsPreviewDate(DateTime.Today);
    }

    /// <summary>
    /// 计算当前时间是否在从前瞻日期开始的2.5天范围内。
    /// </summary>
    /// <returns>如果在范围内，返回 true；否则返回 false。</returns>
    public static bool IsWithinPreviewRange(DateTime now)
    {
        TimeSpan difference = now.Date - StartDate;
        int daysSinceStart = difference.Days;

        if (daysSinceStart < 0)
        {
            return false;
        }

        int intervalCount = daysSinceStart / IntervalDays;
        DateTime lastPreviewDate = StartDate.AddDays(intervalCount * IntervalDays);
        TimeSpan timeSinceLastPreview = now - lastPreviewDate;

        return timeSinceLastPreview.TotalDays >= 0 && timeSinceLastPreview.TotalDays <= ValidDays;
    }
}