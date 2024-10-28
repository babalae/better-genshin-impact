using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Common;

/// <summary>
/// https://stackoverflow.com/questions/1563191/cleanest-way-to-write-retry-logic
/// </summary>
public static class NewRetry
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
        List<System.Exception> exceptions = [];

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
            catch (RetryException ex)
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

    public static async Task<bool> WaitForAction(Func<bool> action, CancellationToken ct, int retryTimes = 10, int delayMs = 1000)
    {
        for (var i = 0; i < retryTimes; i++)
        {
            await TaskControl.Delay(delayMs, ct);
            if (action())
            {
                return true;
            }
        }
        return false;
    }
}
