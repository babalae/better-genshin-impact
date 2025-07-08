using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model.Area;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common;

/// <summary>
/// https://stackoverflow.com/questions/1563191/cleanest-way-to-write-retry-logic
/// </summary>
public static class NewRetry
{
    /// <summary>
    /// 重试指定操作，若抛出 RetryException 则在指定间隔后重试，最多尝试 maxAttemptCount 次。
    /// </summary>
    /// <param name="action">要执行的操作</param>
    /// <param name="retryInterval">重试间隔</param>
    /// <param name="maxAttemptCount">最大尝试次数</param>
    public static void Do(Action action, TimeSpan retryInterval, int maxAttemptCount = 3)
    {
        _ = Do<object?>(() =>
        {
            action();
            return null;
        }, retryInterval, maxAttemptCount);
    }

    /// <summary>
    /// 重试指定操作，若抛出 RetryException 则在指定间隔后重试，最多尝试 maxAttemptCount 次，返回操作结果。
    /// </summary>
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="action">要执行的操作</param>
    /// <param name="retryInterval">重试间隔</param>
    /// <param name="maxAttemptCount">最大尝试次数</param>
    /// <returns>操作结果</returns>
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

    /// <summary>
    /// 重试执行 action，直到返回 true 或达到最大重试次数。
    /// </summary>
    /// <param name="action">判断条件</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="retryTimes">最大重试次数</param>
    /// <param name="delayMs">每次重试间隔（毫秒）</param>
    /// <returns>是否成功</returns>
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
    
    /// <summary>
    /// 重试直到某个元素出现，可执行键盘或鼠标操作。
    /// </summary>
    /// <param name="recognitionObject">要识别的目标对象</param>
    /// <param name="retryAction">每次重试时执行的操作</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="maxAttemptCount">最大尝试次数</param>
    /// <param name="retryInterval">重试间隔(毫秒)</param>
    /// <returns>是否成功找到元素</returns>
    public static async Task<bool> WaitForElementAppear(
        RecognitionObject recognitionObject,
        Action? retryAction,
        CancellationToken ct,
        int maxAttemptCount = 10,
        int retryInterval = 1000
        )
    {
        for (int i = 0; i < maxAttemptCount; i++)
        {
            if (ct.IsCancellationRequested) return false;
            
            // 执行重试操作（如按键）
            retryAction?.Invoke();
            
            // 等待指定时间
            await TaskControl.Delay(retryInterval, ct);
            
            // 截图并查找元素
            using var screen = CaptureToRectArea();
            using var result = screen.Find(recognitionObject);
            
            // 元素已出现
            if (!result.IsEmpty())
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 重试直到某个元素消失，可执行键盘或鼠标操作。
    /// </summary>
    /// <param name="recognitionObject">要识别的目标对象</param>
    /// <param name="retryAction">每次重试时执行的操作</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="maxAttemptCount">最大尝试次数</param>
    /// <param name="retryInterval">重试间隔(毫秒)</param>
    /// <returns>是否成功等待元素消失</returns>
    public static async Task<bool> WaitForElementDisappear(
        RecognitionObject recognitionObject,
        Action? retryAction,
        CancellationToken ct,
        int maxAttemptCount = 10,
        int retryInterval = 1000)
    {
        for (int i = 0; i < maxAttemptCount; i++)
        {
            if (ct.IsCancellationRequested) return false;
            
            // 执行重试操作（如按键）
            retryAction?.Invoke();
            
            // 等待指定时间
            await TaskControl.Delay(retryInterval, ct);
            
            // 截图并查找元素
            using var screen = CaptureToRectArea();
            using var result = screen.Find(recognitionObject);
            
            // 元素已消失
            if (result.IsEmpty())
            {
                return true;
            }
        }
        return false;
    }
    
    public static async Task<bool> WaitForElementDisappear(
        RecognitionObject recognitionObject,
        Action<ImageRegion> retryAction,  // 接收截图的回调
        CancellationToken ct,
        int maxAttemptCount = 10,
        int retryInterval = 1000)
    {
        for (int i = 0; i < maxAttemptCount; i++)
        {
            if (ct.IsCancellationRequested) return false;
            
            // 截图并查找元素
            using var screen = CaptureToRectArea();
            using var result = screen.Find(recognitionObject);
            
            // 元素已消失
            if (result.IsEmpty()) return true;
            
            // 执行重试操作（传入当前截图）
            retryAction?.Invoke(screen);
            
            // 等待指定时间
            await Delay(retryInterval, ct);
        }
        return false;
    }
}

