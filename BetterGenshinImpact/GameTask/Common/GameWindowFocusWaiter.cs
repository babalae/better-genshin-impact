using System;
using System.Threading;

namespace BetterGenshinImpact.GameTask.Common;

internal static class GameWindowFocusWaiter
{
    // 每次恢复后等待一秒；窗口关闭、取消或连续失败时必须停止发送输入。
    internal static void Wait(Func<bool> windowExists, Func<bool> hasFocus,
        Action<int> restoreFocus, CancellationToken ct, Action<CancellationToken>? wait = null)
    {
        wait ??= token =>
        {
            if (token.WaitHandle.WaitOne(1000)) token.ThrowIfCancellationRequested();
        };
        for (var attempt = 0; attempt <= 30; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (!windowExists()) throw new InvalidOperationException("原神窗口已关闭，请重新启动游戏和截图器");
            if (hasFocus()) return;
            if (attempt == 30) throw new TimeoutException("连续恢复原神焦点失败，已停止当前任务");
            restoreFocus(attempt);
            wait(ct);
        }
    }
}
