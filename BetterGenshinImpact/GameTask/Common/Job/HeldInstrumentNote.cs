using System;
using System.Diagnostics;
using BetterGenshinImpact.Core.Input;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.Common.Job;

// 读帧循环在持键期间继续采样。抬起失败时保留本次琴键，以便下一轮重新释放。
internal sealed class HeldInstrumentNote(IKeyboardInput keyboard) : IDisposable
{
    private User32.VK? _key;
    internal bool Held => _key.HasValue;
    internal TimeSpan PressedAt { get; private set; }

    internal void KeyDown(User32.VK key)
    {
        _key = key;
        keyboard.KeyDown(key);
        PressedAt = Stopwatch.GetElapsedTime(0);
    }

    internal void ReleaseIfDue(bool awaitingHit, TimeSpan now)
    {
        if (!Held) return;
        var elapsed = now - PressedAt;
        if (elapsed >= TimeSpan.FromMilliseconds(awaitingHit ? 120 : 35)) Dispose();
    }

    public void Dispose()
    {
        if (_key is not { } key) return;
        keyboard.KeyUp(key);
        _key = null;
    }
}
