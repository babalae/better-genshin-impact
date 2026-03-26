using System;

namespace BetterGenshinImpact.Core.Simulator.Hardware;

internal interface IHardwareKeyboardBackend
{
    bool EnsureConnected();

    void KeyDown(int hidCode);

    void KeyUp(int hidCode);

    void KeyPress(int hidCode);
}

internal interface IHardwareKeyboardStateBackend
{
    bool TryGetKeyState(int hidCode, out HardwareInputState state);
}

internal interface IHardwareMouseBackend
{
    bool EnsureConnected();

    void MouseMoveBy(int dx, int dy);

    void MouseMoveTo(int x, int y);

    void MouseButtonDown(HardwareMouseButton button);

    void MouseButtonUp(HardwareMouseButton button);

    void MouseButtonClick(HardwareMouseButton button, int count);

    void MouseWheelVertical(int delta);

    void MouseWheelHorizontal(int delta);
}

internal interface IHardwareMouseStateBackend
{
    bool TryGetButtonState(HardwareMouseButton button, out HardwareInputState state);
}

internal enum HardwareMouseButton
{
    Left = 0,
    Right = 1,
    Middle = 2,
    Side1 = 3,
    Side2 = 4,
}

internal enum HardwareInputState
{
    None = 0,
    Physical = 1,
    Hardware = 2,
    Both = 3,
}

internal interface IHardwareApiConnection : IDisposable
{
    bool EnsureConnected();
}
