using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Vanara.PInvoke;

namespace Fischless.WindowsInput;

internal class WindowsInputMessageDispatcher : IInputMessageDispatcher
{
    public void DispatchInput(User32.INPUT[] inputs)
    {
        if (inputs == null)
        {
            throw new ArgumentNullException(nameof(inputs));
        }

        if (inputs.Length == 0)
        {
            throw new ArgumentException("The input array was empty", nameof(inputs));
        }

        uint num = User32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(User32.INPUT)));

        if (num != inputs.Length)
        {
            var errorCode = Marshal.GetLastWin32Error();
            using var process = Process.GetCurrentProcess();
            throw new InvalidOperationException(CreateFailureMessage(
                inputs.Length,
                num,
                errorCode,
                new Win32Exception(errorCode).Message,
                process.Id,
                process.SessionId));
        }
    }

    internal static string CreateFailureMessage(
        int requested,
        uint sent,
        int errorCode,
        string errorMessage,
        int processId,
        int sessionId)
    {
        return $"模拟键鼠消息发送失败: requested={requested}, sent={sent}, "
               + $"win32Error={errorCode} ({errorMessage}), pid={processId}, sessionId={sessionId}. "
               + "常见原因包括权限级别不一致或安全软件拦截；UIPI 拦截时 SendInput 可能返回 0，"
               + "但 GetLastError 不一定提供有效原因。";
    }
}
