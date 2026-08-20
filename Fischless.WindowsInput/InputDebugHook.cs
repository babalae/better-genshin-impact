using System;
using System.Diagnostics;

namespace Fischless.WindowsInput;

/// <summary>
/// Debug 下模拟输入埋点钩子。由宿主注册 Handler；Release 调用会被 Conditional 抹掉。
/// Handler 异常会被吞掉，绝不能打断 SendInput。
/// </summary>
public static class InputDebugHook
{
    /// <summary>参数：(action, detail)</summary>
    public static Action<string, string>? Handler { get; set; }

    [Conditional("DEBUG")]
    public static void Record(string action, string detail = "")
    {
#if DEBUG
        var handler = Handler;
        if (handler == null)
        {
            return;
        }

        try
        {
            handler(action, detail ?? string.Empty);
        }
        catch
        {
            // 埋点失败不影响键鼠
        }
#endif
    }
}
