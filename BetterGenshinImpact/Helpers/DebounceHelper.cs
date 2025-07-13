using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Helpers;

// 防抖工具类
public class DebounceHelper
{
    // 用于存储每个 key 的取消令牌
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> Tokens = new();


    /// <summary>
    /// 防抖并异步执行指定的操作，只会在最后一次调用后等待指定时间再执行。
    /// </summary>
    /// <param name="key">防抖操作的唯一标识(脸滚键盘也可以啦)</param>
    /// <param name="action">要执行的操作</param>
    /// <param name="milliseconds">防抖时间（毫秒）</param>
    /// <param name="cts">可选的取消令牌源，如果为 null 则会创建一个新的</param>
    public static async Task DebounceAsync(string key, Action action, int milliseconds,
        CancellationTokenSource? cts = null)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("Key cannot be null or empty.", nameof(key));
        }

        if (action == null)
        {
            throw new ArgumentNullException(nameof(action), "Action cannot be null.");
        }

        if (milliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds), "Milliseconds must be greater than zero.");
        }

        // 如果传入的 CancellationTokenSource 为 null，则创建一个新的
        cts ??= new CancellationTokenSource();

        // 如果已存在相同 key，则取消之前的操作
        if (Tokens.TryGetValue(key, out var oldToken))
        {
            try
            {
                await oldToken.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        Tokens[key] = cts;
        // 延迟指定时间后执行操作，如果期间被取消则不执行
        try
        {
            await Task.Delay(milliseconds, cts.Token).ContinueWith(t =>
                {
                    if (!t.IsCanceled && !cts.Token.IsCancellationRequested)
                    {
                        // 如果没有被取消，则执行操作
                        action();
                    }
                }
            );
        }
        catch (TaskCanceledException)
        {
            // 如果任务被取消，则不执行操作
        }
    }
}