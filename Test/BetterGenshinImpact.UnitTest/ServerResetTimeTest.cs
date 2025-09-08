using System;
using BetterGenshinImpact.Model;

namespace BetterGenshinImpact.UnitTest;

public class ServerResetTimeTests
{
    // CN Server: Monday 4AM GMT+8 → Sunday 8PM UTC (20:00)
    private readonly ServerResetTime _cnServerReset = new()
    {
        DayOfWeek = DayOfWeek.Sunday,
        Hour = 20 // 8PM UTC
    };

    // NA Server: Monday 4AM GMT-5 → Monday 9AM UTC (09:00)
    private readonly ServerResetTime _naServerReset = new()
    {
        DayOfWeek = DayOfWeek.Monday,
        Hour = 9 // 9AM UTC
    };

    [Fact]
    public void IsWeeklyResetHour_CN_Server_ReturnsTrue_AtResetTime()
    {
        // Arrange: Sunday 8PM UTC (CN server reset time)
        var resetTime = new DateTime(2024, 1, 7, 20, 0, 0, DateTimeKind.Utc);

        // Act & Assert
        Assert.True(_cnServerReset.IsWeeklyResetHour(resetTime));
    }

    [Fact]
    public void IsWeeklyResetHour_NA_Server_ReturnsTrue_AtResetTime()
    {
        // Arrange: Monday 9AM UTC (NA server reset time)
        var resetTime = new DateTime(2024, 1, 8, 9, 0, 0, DateTimeKind.Utc);

        // Act & Assert
        Assert.True(_naServerReset.IsWeeklyResetHour(resetTime));
    }

    [Fact]
    public void IsWeeklyResetHour_ReturnsFalse_BeforeResetTime()
    {
        // Arrange: Sunday 7:59PM UTC (1 minute before CN reset)
        var time = new DateTime(2024, 1, 7, 19, 59, 0, DateTimeKind.Utc);

        // Act & Assert
        Assert.False(_cnServerReset.IsWeeklyResetHour(time));
    }

    [Fact]
    public void GetDayOfWeek_CN_Server_BeforeReset_ReturnsPreviousDay()
    {
        // Arrange: Sunday 7PM UTC (before CN reset at 8PM UTC)
        // Server still considers it Saturday
        var time = new DateTime(2024, 1, 7, 19, 0, 0, DateTimeKind.Utc);

        // Act
        var result = _cnServerReset.GetDayOfWeek(time);

        // Assert
        Assert.Equal(DayOfWeek.Saturday, result);
    }

    [Fact]
    public void GetDayOfWeek_CN_Server_AfterReset_ReturnsCurrentDay()
    {
        // Arrange: Sunday 9PM UTC (after CN reset at 8PM UTC)
        // Server considers it Sunday
        var time = new DateTime(2024, 1, 7, 21, 0, 0, DateTimeKind.Utc);

        // Act
        var result = _cnServerReset.GetDayOfWeek(time);

        // Assert
        Assert.Equal(DayOfWeek.Sunday, result);
    }

    [Fact]
    public void GetDayOfWeek_NA_Server_BeforeReset_ReturnsPreviousDay()
    {
        // Arrange: Monday 8AM UTC (before NA reset at 9AM UTC)
        // Server still considers it Sunday
        var time = new DateTime(2024, 1, 8, 8, 0, 0, DateTimeKind.Utc);

        // Act
        var result = _naServerReset.GetDayOfWeek(time);

        // Assert
        Assert.Equal(DayOfWeek.Sunday, result);
    }

    [Fact]
    public void GetDayOfWeek_NA_Server_AfterReset_ReturnsCurrentDay()
    {
        // Arrange: Monday 10AM UTC (after NA reset at 9AM UTC)
        // Server considers it Monday
        var time = new DateTime(2024, 1, 8, 10, 0, 0, DateTimeKind.Utc);

        // Act
        var result = _naServerReset.GetDayOfWeek(time);

        // Assert
        Assert.Equal(DayOfWeek.Monday, result);
    }

    [Fact]
    public void GetNextDailyResetTime_CN_Server_BeforeReset_ReturnsSameDay()
    {
        // Arrange: Sunday 6PM UTC (before CN daily reset at 8PM UTC)
        var time = new DateTime(2024, 1, 7, 18, 0, 0, DateTimeKind.Utc);

        // Act
        var result = _cnServerReset.GetNextDailyResetTime(time);

        // Assert: Should return same day at 8PM UTC
        Assert.Equal(new DateTime(2024, 1, 7, 20, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void GetNextDailyResetTime_CN_Server_AfterReset_ReturnsNextDay()
    {
        // Arrange: Sunday 9PM UTC (after CN daily reset at 8PM UTC)
        var time = new DateTime(2024, 1, 7, 21, 0, 0, DateTimeKind.Utc);

        // Act
        var result = _cnServerReset.GetNextDailyResetTime(time);

        // Assert: Should return next day (Monday) at 8PM UTC
        Assert.Equal(new DateTime(2024, 1, 8, 20, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void GetNextWeeklyResetTime_CN_Server_BeforeReset_ReturnsSameWeek()
    {
        // Arrange: Sunday 6PM UTC (before CN weekly reset at 8PM UTC)
        var time = new DateTime(2024, 1, 7, 18, 0, 0, DateTimeKind.Utc);

        // Act
        var result = _cnServerReset.GetNextWeeklyResetTime(time);

        // Assert: Should return same Sunday at 8PM UTC
        Assert.Equal(new DateTime(2024, 1, 7, 20, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void GetNextWeeklyResetTime_CN_Server_AfterReset_ReturnsNextWeek()
    {
        // Arrange: Sunday 9PM UTC (after CN weekly reset at 8PM UTC)
        var time = new DateTime(2024, 1, 7, 21, 0, 0, DateTimeKind.Utc);

        // Act
        var result = _cnServerReset.GetNextWeeklyResetTime(time);

        // Assert: Should return next Sunday at 8PM UTC
        Assert.Equal(new DateTime(2024, 1, 14, 20, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void GetNextWeeklyResetTime_NA_Server_BeforeReset_ReturnsSameWeek()
    {
        // Arrange: Monday 8AM UTC (before NA weekly reset at 9AM UTC)
        var time = new DateTime(2024, 1, 8, 8, 0, 0, DateTimeKind.Utc);

        // Act
        var result = _naServerReset.GetNextWeeklyResetTime(time);

        // Assert: Should return same Monday at 9AM UTC
        Assert.Equal(new DateTime(2024, 1, 8, 9, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void GetNextWeeklyResetTime_NA_Server_AfterReset_ReturnsNextWeek()
    {
        // Arrange: Monday 10AM UTC (after NA weekly reset at 9AM UTC)
        var time = new DateTime(2024, 1, 8, 10, 0, 0, DateTimeKind.Utc);

        // Act
        var result = _naServerReset.GetNextWeeklyResetTime(time);

        // Assert: Should return next Monday at 9AM UTC
        Assert.Equal(new DateTime(2024, 1, 15, 9, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void GetNextWeeklyResetTime_CN_Server_MidWeek_ReturnsNextResetDay()
    {
        // Arrange: Wednesday 12PM UTC (mid-week, CN reset is Sunday)
        var time = new DateTime(2024, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var result = _cnServerReset.GetNextWeeklyResetTime(time);

        // Assert: Should return next Sunday at 8PM UTC
        Assert.Equal(new DateTime(2024, 1, 14, 20, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void GetNextWeeklyResetTime_NA_Server_Friday_ReturnsNextMonday()
    {
        // Arrange: Friday 12PM UTC (NA reset is Monday)
        var time = new DateTime(2024, 1, 12, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var result = _naServerReset.GetNextWeeklyResetTime(time);

        // Assert: Should return next Monday at 9AM UTC
        Assert.Equal(new DateTime(2024, 1, 15, 9, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void GetNextWeeklyResetTime_EdgeCase_ExactlyAtResetTime()
    {
        // Arrange: Exactly at CN reset time (Sunday 8PM UTC)
        var time = new DateTime(2024, 1, 7, 20, 0, 0, DateTimeKind.Utc);

        // Act
        var result = _cnServerReset.GetNextWeeklyResetTime(time);

        // Assert: Should return next week's reset (since current time is exactly at reset)
        Assert.Equal(new DateTime(2024, 1, 14, 20, 0, 0, DateTimeKind.Utc), result);
    }
}