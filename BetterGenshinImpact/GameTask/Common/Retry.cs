using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Common;

/// <summary>
/// https://stackoverflow.com/questions/1563191/cleanest-way-to-write-retry-logic
/// </summary>
public static class Retry
{
    public static void Do(Action action, TimeSpan retryInterval, int maxAttemptCount = 3)
    {
        _ = Do<object?>(() =>
        {
            action();
            return null;
        }, retryInterval, maxAttemptCount);
    }

    public static T Do<T>(Func<T> action, TimeSpan retryInterval, int maxAttemptCount = 3)
    {
        List<Exception> exceptions = [];

        for (int attempted = 0; attempted < maxAttemptCount; attempted++)
        {
            try
            {
                if (attempted > 0)
                {
                    Thread.Sleep(retryInterval);
                }

                return action();
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        if (exceptions.Count > 0)
        {
            throw exceptions.Last();
        }
        throw new AggregateException(exceptions);
    }

    public static async Task DoAsync(Func<Task> action, TimeSpan retryInterval, int maxAttemptCount = 3)
    {
        _ = await DoAsync<object?>(async () =>
        {
            await action();
            return null;
        }, retryInterval, maxAttemptCount);
    }

    public static async Task<T> DoAsync<T>(Func<Task<T>> action, TimeSpan retryInterval, int maxAttemptCount = 3)
    {
        List<Exception> exceptions = [];

        for (int attempted = 0; attempted < maxAttemptCount; attempted++)
        {
            try
            {
                if (attempted > 0)
                {
                    await Task.Delay(retryInterval);
                }

                return await action();
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        if (exceptions.Count > 0)
        {
            throw exceptions.Last();
        }
        throw new AggregateException(exceptions);
    }
}
