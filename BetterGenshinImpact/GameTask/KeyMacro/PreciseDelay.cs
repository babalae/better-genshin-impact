using Microsoft.Win32.SafeHandles;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace BetterGenshinImpact.GameTask.KeyMacro;

/// <summary>
/// 高精度、可立即中断的等待。
/// 项目没有调用 timeBeginPeriod，Thread.Sleep 的精度受系统计时器影响（默认约 15.6 ms），
/// 这里改用高精度可等待计时器（Windows 10 1803 起支持），不支持时退回 WaitHandle.WaitOne
/// </summary>
internal static class PreciseDelay
{
    /// <summary>
    /// 每段最长等待时间。段间检查停止条件，保证松开热键或切出游戏后能及时停止
    /// </summary>
    private const double SliceMilliseconds = 50;

    /// <summary>
    /// CreateWaitableTimerExW 的高精度标志
    /// </summary>
    private const uint CreateWaitableTimerHighResolution = 0x00000002;

    /// <summary>
    /// 计时器的全部访问权限
    /// </summary>
    private const uint TimerAllAccess = 0x1F0003;

    /// <summary>
    /// 当前线程复用的计时器。执行线程结束前调用 <see cref="ReleaseThreadTimer"/> 关闭
    /// </summary>
    [ThreadStatic]
    private static TimerWaitHandle? _timer;

    /// <summary>
    /// 当前线程创建计时器是否已失败，失败后不再重试
    /// </summary>
    [ThreadStatic]
    private static bool _timerUnavailable;

    /// <summary>
    /// 等待指定毫秒数
    /// </summary>
    /// <param name="milliseconds">等待时长，小于等于 0 时只检查一次停止条件</param>
    /// <param name="ct">取消令牌，取消时立即返回</param>
    /// <param name="shouldContinue">停止条件，返回 false 时立即结束等待</param>
    /// <returns>等待完整结束返回 true；被取消或停止条件不满足时返回 false</returns>
    public static bool Wait(int milliseconds, CancellationToken ct, Func<bool> shouldContinue)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(milliseconds * (double)Stopwatch.Frequency / 1000);
        while (true)
        {
            if (ct.IsCancellationRequested || !shouldContinue())
            {
                return false;
            }

            // 用截止时间计算剩余时长，分段等待不累积误差
            var remaining = (deadline - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency;
            if (remaining <= 0)
            {
                return true;
            }

            if (!WaitSlice(Math.Min(remaining, SliceMilliseconds), ct))
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 关闭当前线程的计时器
    /// </summary>
    public static void ReleaseThreadTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>
    /// 等待一段时间，被取消时返回 false
    /// </summary>
    private static bool WaitSlice(double milliseconds, CancellationToken ct)
    {
        var timer = GetTimer();
        if (timer != null)
        {
            // 以 100 纳秒为单位，负数表示相对时间
            var dueTime = -Math.Max(1L, (long)(milliseconds * 10_000));
            if (SetWaitableTimer(timer.SafeWaitHandle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
            {
                return WaitHandle.WaitAny([timer, ct.WaitHandle]) == 0;
            }
        }

        return !ct.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(milliseconds));
    }

    /// <summary>
    /// 取得当前线程的高精度计时器，不支持时返回 null
    /// </summary>
    private static TimerWaitHandle? GetTimer()
    {
        if (_timer != null || _timerUnavailable)
        {
            return _timer;
        }

        var handle = CreateWaitableTimerExW(IntPtr.Zero, null, CreateWaitableTimerHighResolution, TimerAllAccess);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            _timerUnavailable = true;
            return null;
        }

        _timer = new TimerWaitHandle(handle);
        return _timer;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr lpTimerAttributes, string? lpTimerName, uint dwFlags,
        uint dwDesiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle hTimer, ref long pDueTime, int lPeriod, IntPtr pfnCompletionRoutine,
        IntPtr lpArgToCompletionRoutine, [MarshalAs(UnmanagedType.Bool)] bool fResume);

    /// <summary>
    /// 包装计时器句柄，便于与取消令牌一起 WaitAny
    /// </summary>
    private sealed class TimerWaitHandle : WaitHandle
    {
        /// <summary>
        /// 接管计时器句柄
        /// </summary>
        public TimerWaitHandle(SafeWaitHandle handle)
        {
            SafeWaitHandle = handle;
        }
    }
}
