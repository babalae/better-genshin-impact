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

        if (num != (ulong)(long)inputs.Length)
        {
            throw new Exception("模拟键鼠消息发送失败！常见原因：1.你未以管理员权限运行程序；2.存在安全软件拦截（比如360）");
        }
    }
}
