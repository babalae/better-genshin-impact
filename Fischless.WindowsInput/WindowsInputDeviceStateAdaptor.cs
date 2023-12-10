using Vanara.PInvoke;

namespace Fischless.WindowsInput;

public class WindowsInputDeviceStateAdaptor : IInputDeviceStateAdaptor
{
    public bool IsKeyDown(User32.VK keyCode)
    {
        short keyState = User32.GetKeyState((ushort)keyCode);
        return keyState < 0;
    }

    public bool IsKeyUp(User32.VK keyCode)
    {
        return !IsKeyDown(keyCode);
    }

    public bool IsHardwareKeyDown(User32.VK keyCode)
    {
        short asyncKeyState = User32.GetAsyncKeyState((ushort)keyCode);
        return asyncKeyState < 0;
    }

    public bool IsHardwareKeyUp(User32.VK keyCode)
    {
        return !IsHardwareKeyDown(keyCode);
    }

    public bool IsTogglingKeyInEffect(User32.VK keyCode)
    {
        short keyState = User32.GetKeyState((ushort)keyCode);
        return (keyState & 1) == 1;
    }
}
