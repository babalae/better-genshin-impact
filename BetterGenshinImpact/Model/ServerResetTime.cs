using System;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Model;

/// <summary>
/// A custom converter for the ServerResetTime struct to handle JSON serialization.
/// </summary>
public class ServerResetTimeJsonConverter : JsonConverter<ServerResetTime>
{
    /// <summary>
    /// Reads a JSON string and converts it to a ServerResetTime struct.
    /// </summary>
    /// <param name="reader">The Utf8JsonReader to read from.</param>
    /// <param name="typeToConvert">The type of object to convert.</param>
    /// <param name="options">The JsonSerializerOptions.</param>
    /// <returns>The deserialized ServerResetTime struct.</returns>
    public override ServerResetTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("ServerResetTime expected to be a string.");
        }

        var value = reader.GetString();
        return ServerResetTime.Parse(value!);
    }

    /// <summary>
    /// Writes a ServerResetTime struct to a JSON string.
    /// </summary>
    /// <param name="writer">The Utf8JsonWriter to write to.</param>
    /// <param name="value">The ServerResetTime struct to serialize.</param>
    /// <param name="options">The JsonSerializerOptions.</param>
    public override void Write(Utf8JsonWriter writer, ServerResetTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

/// <summary>
/// A custom converter for the ServerResetTime struct to handle WPF data binding conversions.
/// </summary>
public class ServerResetTimeTypeConverter : TypeConverter
{
    /// <summary>
    /// Indicates whether this converter can convert an object of the given type to the type of this converter.
    /// </summary>
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string);
    }

    /// <summary>
    /// Converts the given value to the type of this converter.
    /// </summary>
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string stringValue)
        {
            return ServerResetTime.Parse(stringValue);
        }

        return base.ConvertFrom(context, culture, value);
    }

    /// <summary>
    /// Indicates whether this converter can convert the object to the specified type.
    /// </summary>
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        return destinationType == typeof(string);
    }

    /// <summary>
    /// Converts the given value to the specified type.
    /// </summary>
    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value,
        Type destinationType)
    {
        if (destinationType == typeof(string) && value is ServerResetTime resetTime)
        {
            return resetTime.ToString();
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}

/// <summary>
/// Represents a server reset time with day of week and hour components.
/// Used to configure regional server reset times in UTC.
/// </summary>
[Serializable]
[JsonConverter(typeof(ServerResetTimeJsonConverter))]
[TypeConverter(typeof(ServerResetTimeTypeConverter))]
public readonly record struct ServerResetTime
{
    /// <summary>
    /// The day of the week when the reset occurs.
    /// </summary>
    public DayOfWeek DayOfWeek { get; init; }

    /// <summary>
    /// the hour (in UTC) when the reset occurs (0-23).
    /// </summary>
    public int Hour { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerResetTime"/> struct.
    /// </summary>
    /// <param name="dayOfWeek">The day of the week when reset occurs.</param>
    /// <param name="hour">The hour in UTC when reset occurs (0-23).</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when hour is not between 0 and 23.</exception>
    public ServerResetTime(DayOfWeek dayOfWeek, int hour)
    {
        if (hour < 0 || hour > 23)
            throw new ArgumentOutOfRangeException(nameof(hour), "Hour must be between 0 and 23");

        DayOfWeek = dayOfWeek;
        Hour = hour;
    }

    /// <summary>
    /// Determines whether the specified time matches this reset time.
    /// </summary>
    /// <param name="time">The time to check (should be in UTC).</param>
    /// <returns>True if the time matches the reset time; otherwise, false.</returns>
    public bool IsResetTime(DateTime time)
    {
        return time.DayOfWeek == DayOfWeek && time.Hour == Hour;
    }

    /// <summary>
    /// Determines whether the current UTC time matches this reset time.
    /// </summary>
    /// <returns>True if current time matches the reset time; otherwise, false.</returns>
    public bool IsCurrentResetTime()
    {
        return IsResetTime(DateTime.UtcNow);
    }

    /// <summary>
    /// Returns the server day of week for the given UTC time based on the stored reset day and hour.
    /// </summary>
    /// <param name="referenceTime">The UTC time to evaluate.</param>
    /// <returns>The DayOfWeek representing the server's adjusted day.</returns>
    private DayOfWeek GetServerDayOfWeek(DateTime referenceTime)
    {
        // Ensure the reference time is in UTC for consistency
        var now = referenceTime.ToUniversalTime();
        var dayDifference = ((int)now.DayOfWeek - (int)DayOfWeek + 7) % 7;
        var resetDateTime = referenceTime.Date.AddHours(Hour).AddDays(-dayDifference);

        if (referenceTime < resetDateTime)
            return (DayOfWeek)(((int)referenceTime.DayOfWeek - 1 + 7) % 7);

        return referenceTime.DayOfWeek;
    }

    /// <summary>
    /// Checks if the provided UTC time is server's Monday relative to the reset time.
    /// Monday is when weekly resets occur.
    /// </summary>
    /// <param name="referenceTime">The UTC time to check.</param>
    /// <returns>True if the time falls within server Monday; otherwise, false.</returns>
    public bool IsMonday(DateTime referenceTime)
    {
        return GetServerDayOfWeek(referenceTime) == DayOfWeek.Monday;
    }

    /// <summary>
    /// Checks if the current UTC time is server's Monday relative to the reset time.
    /// Monday is when weekly resets occur.
    /// </summary>
    /// <returns>True if current time falls within server Monday; otherwise, false.</returns>
    public bool IsCurrentMonday() => IsMonday(DateTime.UtcNow);

    /// <summary>
    /// Checks if the provided UTC time is server's Tuesday relative to the reset time.
    /// </summary>
    /// <param name="referenceTime">The UTC time to check.</param>
    /// <returns>True if the time falls within server Tuesday; otherwise, false.</returns>
    public bool IsTuesday(DateTime referenceTime)
    {
        return GetServerDayOfWeek(referenceTime) == DayOfWeek.Tuesday;
    }

    /// <summary>
    /// Checks if the current UTC time is server's Tuesday relative to the reset time.
    /// </summary>
    /// <returns>True if current time falls within server Tuesday; otherwise, false.</returns>
    public bool IsCurrentTuesday() => IsTuesday(DateTime.UtcNow);

    /// <summary>
    /// Checks if the provided UTC time is server's Wednesday relative to the reset time.
    /// </summary>
    /// <param name="referenceTime">The UTC time to check.</param>
    /// <returns>True if the time falls within server Wednesday; otherwise, false.</returns>
    public bool IsWednesday(DateTime referenceTime)
    {
        return GetServerDayOfWeek(referenceTime) == DayOfWeek.Wednesday;
    }

    /// <summary>
    /// Checks if the current UTC time is server's Wednesday relative to the reset time.
    /// </summary>
    /// <returns>True if current time falls within server Wednesday; otherwise, false.</returns>
    public bool IsCurrentWednesday() => IsWednesday(DateTime.UtcNow);

    /// <summary>
    /// Checks if the provided UTC time is server's Thursday relative to the reset time.
    /// </summary>
    /// <param name="referenceTime">The UTC time to check.</param>
    /// <returns>True if the time falls within server Thursday; otherwise, false.</returns>
    public bool IsThursday(DateTime referenceTime)
    {
        return GetServerDayOfWeek(referenceTime) == DayOfWeek.Thursday;
    }

    /// <summary>
    /// Checks if the current UTC time is server's Thursday relative to the reset time.
    /// </summary>
    /// <returns>True if current time falls within server Thursday; otherwise, false.</returns>
    public bool IsCurrentThursday() => IsThursday(DateTime.UtcNow);

    /// <summary>
    /// Checks if the provided UTC time is server's Friday relative to the reset time.
    /// </summary>
    /// <param name="referenceTime">The UTC time to check.</param>
    /// <returns>True if the time falls within server Friday; otherwise, false.</returns>
    public bool IsFriday(DateTime referenceTime)
    {
        return GetServerDayOfWeek(referenceTime) == DayOfWeek.Friday;
    }

    /// <summary>
    /// Checks if the current UTC time is server's Friday relative to the reset time.
    /// </summary>
    /// <returns>True if current time falls within server Friday; otherwise, false.</returns>
    public bool IsCurrentFriday() => IsFriday(DateTime.UtcNow);

    /// <summary>
    /// Checks if the provided UTC time is server's Saturday relative to the reset time.
    /// </summary>
    /// <param name="referenceTime">The UTC time to check.</param>
    /// <returns>True if the time falls within server Saturday; otherwise, false.</returns>
    public bool IsSaturday(DateTime referenceTime)
    {
        return GetServerDayOfWeek(referenceTime) == DayOfWeek.Saturday;
    }

    /// <summary>
    /// Checks if the current UTC time is server's Saturday relative to the reset time.
    /// </summary>
    /// <returns>True if current time falls within server Saturday; otherwise, false.</returns>
    public bool IsCurrentSaturday() => IsSaturday(DateTime.UtcNow);

    /// <summary>
    /// Checks if the provided UTC time is server's Sunday relative to the reset time.
    /// Sunday is when all domain rewards are available.
    /// </summary>
    /// <param name="referenceTime">The UTC time to check.</param>
    /// <returns>True if the time falls within server Sunday; otherwise, false.</returns>
    public bool IsSunday(DateTime referenceTime)
    {
        return GetServerDayOfWeek(referenceTime) == DayOfWeek.Sunday;
    }

    /// <summary>
    /// Checks if the current UTC time is server's Sunday relative to the reset time.
    /// Sunday is when all domain rewards are available.
    /// </summary>
    /// <returns>True if current time falls within server Sunday; otherwise, false.</returns>
    public bool IsCurrentSunday() => IsSunday(DateTime.UtcNow);

    /// <summary>
    /// Calculates the next occurrence of this reset time based on the daily reset.
    ///
    /// This method returns the next reset time of day (hour of day) regardless of week day, relative to a provided time.
    /// </summary>
    /// <param name="referenceTime">The time to use as the reference point for the calculation.</param>
    /// <returns>A DateTime representing the next daily reset time in UTC.</returns>
    public DateTime GetNextDailyResetTime(DateTime referenceTime)
    {
        // Ensure the reference time is in UTC for consistency
        var now = referenceTime.ToUniversalTime();
        var nextReset = new DateTime(now.Year, now.Month, now.Day, Hour, 0, 0, DateTimeKind.Utc);

        // If the reset time for today has already passed, get the one for tomorrow
        if (nextReset <= now)
            nextReset = nextReset.AddDays(1);

        return nextReset;
    }

    /// <summary>
    /// Calculates the next occurrence of the weekly server reset based on stored reset day and hour.
    ///
    /// Returns the next reset DateTime in UTC which is the next instance of the reset day of week at reset hour, relative to the current UTC time.
    /// </summary>
    /// <returns>A DateTime representing the next weekly reset time in UTC.</returns>
    public DateTime GetNextWeeklyResetTime()
    {
        return GetNextWeeklyResetTime(DateTime.UtcNow);
    }

    /// <summary>
    /// Calculates the next occurrence of the weekly server reset based on stored reset day and hour.
    ///
    /// Returns the next reset DateTime in UTC which is the next instance of the reset day of week at reset hour, relative to a provided time.
    /// </summary>
    /// <param name="referenceTime">The time to use as the reference point for the calculation.</param>
    /// <returns>A DateTime representing the next weekly reset time in UTC.</returns>
    public DateTime GetNextWeeklyResetTime(DateTime referenceTime)
    {
        // Ensure the reference time is in UTC for consistency
        var now = referenceTime.ToUniversalTime();

        // Start from the reference date at the reset hour
        var nextReset = new DateTime(now.Year, now.Month, now.Day, Hour, 0, 0, DateTimeKind.Utc);

        // Calculate how many days to add to get to the correct DayOfWeek
        var daysToAdd = ((int)DayOfWeek - (int)nextReset.DayOfWeek + 7) % 7;
        nextReset = nextReset.AddDays(daysToAdd);

        // If the calculated date is not strictly in the future, add one week
        if (nextReset <= now)
            nextReset = nextReset.AddDays(7);

        return nextReset;
    }


    /// <summary>
    /// Returns a string representation of the reset time in format "dayAbbr;hour".
    /// Uses English day abbreviations for consistency across locales.
    /// </summary>
    /// <returns>A string in format like "sun;20".</returns>
    public override string ToString()
    {
        var culture = new CultureInfo("en-US");
        var dayAbbr = culture.DateTimeFormat.GetAbbreviatedDayName(DayOfWeek).ToLower();
        return $"{dayAbbr};{Hour}";
    }

    /// <summary>
    /// Parses a string representation of a server reset time.
    /// </summary>
    /// <param name="value">The string to parse, expected format "dayAbbr;hour".</param>
    /// <returns>A ServerResetTime struct representing the parsed values.</returns>
    /// <exception cref="ArgumentException">Thrown when value is null or empty.</exception>
    /// <exception cref="FormatException">Thrown when the format is invalid or values cannot be parsed.</exception>
    public static ServerResetTime Parse(string value)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("Value cannot be null or empty", nameof(value));

        var parts = value.Split(';');
        if (parts.Length != 2)
            throw new FormatException("Invalid format. Expected 'day;hour'");

        var dayPart = parts[0].Trim().ToLower();
        if (!int.TryParse(parts[1].Trim(), out var hour) || hour < 0 || hour > 23)
            throw new FormatException("Invalid hour format. Must be between 0 and 23");

        var dayOfWeek = dayPart switch
        {
            "sun" or "sunday" => DayOfWeek.Sunday,
            "mon" or "monday" => DayOfWeek.Monday,
            "tue" or "tuesday" => DayOfWeek.Tuesday,
            "wed" or "wednesday" => DayOfWeek.Wednesday,
            "thu" or "thursday" => DayOfWeek.Thursday,
            "fri" or "friday" => DayOfWeek.Friday,
            "sat" or "saturday" => DayOfWeek.Saturday,
            _ => throw new FormatException(
                $"Invalid day format: {dayPart}. Expected English abbreviation like 'sun', 'mon', etc.")
        };

        return new ServerResetTime(dayOfWeek, hour);
    }
}