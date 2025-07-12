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
    /// 防抖执行指定的操作，只会在最后一次调用后等待指定时间再执行。
    /// </summary>
    /// <param name="key">防抖操作的唯一标识(脸滚键盘也可以啦)</param>
    /// <param name="action">要执行的操作</param>
    /// <param name="milliseconds">防抖时间（毫秒）</param>
    public static void Debounce(string key, Action action, int milliseconds)
    {
        // 如果已存在相同 key，则取消之前的操作
        if (Tokens.TryGetValue(key, out var oldToken))
        {
            oldToken.Cancel();
        }

        var cts = new CancellationTokenSource();
        Tokens[key] = cts;

        // 延迟指定时间后执行操作，如果期间被取消则不执行
        Task.Delay(milliseconds, cts.Token)
            .ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    action();
                }
                // 执行后移除 key
                Tokens.TryRemove(key, out _);
            }, TaskScheduler.Default);
    }
}