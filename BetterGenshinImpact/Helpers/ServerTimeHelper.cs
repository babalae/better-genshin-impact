using System;
using BetterGenshinImpact.GameTask;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// Provides the current time in the configured server's timezone.
/// </summary>
public interface IServerTimeProvider
{
    /// <summary>
    /// Gets the current time adjusted to the server's timezone offset.
    /// </summary>
    /// <returns>A <see cref="DateTimeOffset"/> representing the current server time.</returns>
    DateTimeOffset GetServerTimeNow();
}

/// <inheritdoc cref="IServerTimeProvider"/>
/// <remarks>
/// This implementation uses a <see cref="TimeProvider"/> to get the current UTC time
/// and then applies the configured server timezone offset to it.
/// </remarks>
public class ServerTimeProvider : IServerTimeProvider
{
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerTimeProvider"/> class.
    /// </summary>
    /// <param name="timeProvider">The <see cref="TimeProvider"/> used to retrieve the base UTC time.</param>
    public ServerTimeProvider(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public DateTimeOffset GetServerTimeNow()
    {
        var serverOffset = GetServerOffset();
        return _timeProvider.GetUtcNow().ToOffset(serverOffset);
    }

    private static TimeSpan GetServerOffset()
    {
        try
        {
            return TaskContext.Instance().Config.OtherConfig.ServerTimeZoneOffset;
        }
        // throw new Exception("Config未初始化"); in TaskContext.cs
        catch (Exception)
        {
            // Assume Beijing timezone for core developers' testing if config isn't loaded
            return TimeSpan.FromHours(8);
        }
    }
}

/// <summary>
/// Provides a static facade for easy access to the server time.
/// </summary>
public static class ServerTimeHelper
{
    private static IServerTimeProvider? _serverTimeProvider;

    /// <summary>
    /// Initializes the static helper with a concrete <see cref="IServerTimeProvider"/> implementation.
    /// This method must be called once during application startup.
    /// </summary>
    /// <param name="serverTimeProvider">The provider to use for retrieving server time.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="serverTimeProvider"/> is null.</exception>
    public static void Initialize(IServerTimeProvider serverTimeProvider)
    {
        _serverTimeProvider = serverTimeProvider ?? throw new ArgumentNullException(nameof(serverTimeProvider));
    }

    /// <summary>
    /// Gets the current time adjusted to the server's timezone offset.
    /// </summary>
    /// <returns>A <see cref="DateTimeOffset"/> representing the current server time.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <see cref="Initialize"/> has not been called.
    /// </exception>
    public static DateTimeOffset GetServerTimeNow()
    {
        if (_serverTimeProvider is null)
        {
            throw new InvalidOperationException(
                $"{nameof(ServerTimeHelper)} has not been initialized. Call {nameof(Initialize)} first.");
        }

        return _serverTimeProvider.GetServerTimeNow();
    }
}