using System;
using System.Threading;

namespace BetterGenshinImpact.Core.Input;

/// <summary>无人值守运行的输入准入门；抬键清理不经过此门。</summary>
public static class InputSafetyGate
{
    /// <summary>当前唯一游戏运行的输入检查回调。</summary>
    private static Action? _check;

    /// <summary>每次新增输入前同步检查，用户活动发生后不再等待下一个调度周期。</summary>
    public static void Check() => Volatile.Read(ref _check)?.Invoke();

    /// <summary>建立独占的安全作用域，退出时移除自己的检查。</summary>
    public static IDisposable Enter(Action check)
    {
        if (Interlocked.CompareExchange(ref _check, check, null) is not null)
            throw new InvalidOperationException("输入安全作用域已被其他运行占用。");
        // 老执行器直接使用 SendInput 也必须经过相同安全门，不仅保护新 InputHub 通道。
        Volatile.Write(ref Fischless.WindowsInput.InputInjectionTag.BeforeInput, Check);
        return new Scope(check);
    }

    /// <summary>仅释放本次注册，不影响后来建立的作用域。</summary>
    private sealed class Scope(Action check) : IDisposable
    {
        /// <summary>移除安全回调。</summary>
        public void Dispose() => Interlocked.CompareExchange(ref _check, null, check);
    }
}
