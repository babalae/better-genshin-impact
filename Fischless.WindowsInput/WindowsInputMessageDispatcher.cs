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

        // 仅给本程序的 SendInput 打标，避免无人值守运行误把自身输入当成用户返回。
        for (var i = 0; i < inputs.Length; i++)
        {
            if (inputs[i].type == User32.INPUTTYPE.INPUT_KEYBOARD)
            {
                var keyboard = inputs[i].ki;
                if ((keyboard.dwFlags & User32.KEYEVENTF.KEYEVENTF_KEYUP) == 0)
                    System.Threading.Volatile.Read(ref InputInjectionTag.BeforeInput)?.Invoke();
                keyboard.dwExtraInfo = InputInjectionTag.Value;
                inputs[i].ki = keyboard;
            }
            else if (inputs[i].type == User32.INPUTTYPE.INPUT_MOUSE)
            {
                var mouse = inputs[i].mi;
                // 纯抬键输入必须允许清理；移动、按下和滚轮在发送前重新准入。
                if (((uint)mouse.dwFlags & ~0x154u) != 0)
                    System.Threading.Volatile.Read(ref InputInjectionTag.BeforeInput)?.Invoke();
                mouse.dwExtraInfo = InputInjectionTag.Value;
                inputs[i].mi = mouse;
            }
        }
        uint num = User32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(User32.INPUT)));

        if (num != (ulong)(long)inputs.Length)
        {
            throw new Exception("模拟键鼠消息发送失败！常见原因：1.你未以管理员权限运行程序；2.存在安全软件拦截（比如360）");
        }
    }
}
