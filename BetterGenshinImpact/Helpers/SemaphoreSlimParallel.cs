using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Helpers;

public class SemaphoreSlimParallel : IAsyncDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly List<Task> _tasks = [];

    private SemaphoreSlimParallel(int maxDegreeOfParallelism)
    {
        _semaphore = new SemaphoreSlim(maxDegreeOfParallelism);
    }

    public static async Task<SemaphoreSlimParallel> ForEach<T>(
        IEnumerable<T> items,
        Func<T, Task> action,
        int maxDegreeOfParallelism,
        CancellationToken cancellationToken = default)
    {
        var parallelExecutor = new SemaphoreSlimParallel(maxDegreeOfParallelism);

        foreach (var item in items)
        {
            await parallelExecutor._semaphore.WaitAsync(cancellationToken);
            var task = Task.Run(async () =>
            {
                try
                {
                    await action(item);
                }
                finally
                {
                    parallelExecutor._semaphore.Release();
                }
            }, cancellationToken);

            parallelExecutor._tasks.Add(task);
        }

        return parallelExecutor;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // 等待所有任务完成
            await Task.WhenAll(_tasks);
        }
        finally
        {
            _semaphore.Dispose();
        }
    }
}
