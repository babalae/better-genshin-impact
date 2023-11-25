using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;

namespace BetterGenshinImpact.GameTask.AutoWood.Utils
{
    /// <summary>
    /// https://stackoverflow.com/questions/1563191/cleanest-way-to-write-retry-logic
    /// </summary>
    public static class NewRetry
    {
        public static void Do(
            Action action,
            TimeSpan retryInterval,
            int maxAttemptCount = 3)
        {
            Do<object>(() =>
            {
                action();
                return null;
            }, retryInterval, maxAttemptCount);
        }

        public static T Do<T>(
            Func<T> action,
            TimeSpan retryInterval,
            int maxAttemptCount = 3)
        {
            var exceptions = new List<RetryException>();

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
    }
}