using BetterGenshinImpact.Helpers;
using Fischless.WindowsInput;
using System;
using System.Threading;

namespace BetterGenshinImpact.Core.Simulator.Hardware;

internal sealed class RoutingMouseSimulator : IMouseSimulator
{
    private IInputSimulator? _owner;
    private MouseSimulator? _virtualMouse;

    public IKeyboardSimulator Keyboard => _owner?.Keyboard ?? throw new InvalidOperationException("Routing mouse simulator is not initialized.");

    public void Initialize(IInputSimulator owner)
    {
        _owner = owner;
        _virtualMouse = new MouseSimulator(owner);
    }

    public IMouseSimulator MoveMouseBy(int pixelDeltaX, int pixelDeltaY)
    {
        if (TryUseHardware(backend =>
        {
            backend.MouseMoveBy(pixelDeltaX, pixelDeltaY);
            return true;
        }))
        {
            return this;
        }

        Virtual.MoveMouseBy(pixelDeltaX, pixelDeltaY);
        return this;
    }

    public IMouseSimulator MoveMouseTo(double absoluteX, double absoluteY)
    {
        if (TryUseHardware(backend =>
        {
            backend.MouseMoveTo(ToScreenX(absoluteX), ToScreenY(absoluteY));
            return true;
        }))
        {
            return this;
        }

        Virtual.MoveMouseTo(absoluteX, absoluteY);
        return this;
    }

    public IMouseSimulator MoveMouseToPositionOnVirtualDesktop(double absoluteX, double absoluteY)
    {
        if (TryUseHardware(backend =>
        {
            backend.MouseMoveTo(ToScreenX(absoluteX), ToScreenY(absoluteY));
            return true;
        }))
        {
            return this;
        }

        Virtual.MoveMouseToPositionOnVirtualDesktop(absoluteX, absoluteY);
        return this;
    }

    public IMouseSimulator LeftButtonDown() => MouseButtonDown(HardwareMouseButton.Left, m => m.LeftButtonDown());

    public IMouseSimulator LeftButtonUp() => MouseButtonUp(HardwareMouseButton.Left, m => m.LeftButtonUp());

    public IMouseSimulator LeftButtonClick() => MouseButtonClick(HardwareMouseButton.Left, 1, m => m.LeftButtonClick());

    public IMouseSimulator LeftButtonDoubleClick() => MouseButtonClick(HardwareMouseButton.Left, 2, m => m.LeftButtonDoubleClick());

    public IMouseSimulator MiddleButtonDown() => MouseButtonDown(HardwareMouseButton.Middle, m => m.MiddleButtonDown());

    public IMouseSimulator MiddleButtonUp() => MouseButtonUp(HardwareMouseButton.Middle, m => m.MiddleButtonUp());

    public IMouseSimulator MiddleButtonClick() => MouseButtonClick(HardwareMouseButton.Middle, 1, m => m.MiddleButtonClick());

    public IMouseSimulator MiddleButtonDoubleClick() => MouseButtonClick(HardwareMouseButton.Middle, 2, m => m.MiddleButtonDoubleClick());

    public IMouseSimulator RightButtonDown() => MouseButtonDown(HardwareMouseButton.Right, m => m.RightButtonDown());

    public IMouseSimulator RightButtonUp() => MouseButtonUp(HardwareMouseButton.Right, m => m.RightButtonUp());

    public IMouseSimulator RightButtonClick() => MouseButtonClick(HardwareMouseButton.Right, 1, m => m.RightButtonClick());

    public IMouseSimulator RightButtonDoubleClick() => MouseButtonClick(HardwareMouseButton.Right, 2, m => m.RightButtonDoubleClick());

    public IMouseSimulator XButtonDown(int buttonId) => MouseButtonDown(ToXButton(buttonId), m => m.XButtonDown(buttonId));

    public IMouseSimulator XButtonUp(int buttonId) => MouseButtonUp(ToXButton(buttonId), m => m.XButtonUp(buttonId));

    public IMouseSimulator XButtonClick(int buttonId) => MouseButtonClick(ToXButton(buttonId), 1, m => m.XButtonClick(buttonId));

    public IMouseSimulator XButtonDoubleClick(int buttonId) => MouseButtonClick(ToXButton(buttonId), 2, m => m.XButtonDoubleClick(buttonId));

    public IMouseSimulator VerticalScroll(int scrollAmountInClicks)
    {
        if (TryUseHardware(backend =>
        {
            backend.MouseWheelVertical(scrollAmountInClicks);
            return true;
        }))
        {
            return this;
        }

        Virtual.VerticalScroll(scrollAmountInClicks);
        return this;
    }

    public IMouseSimulator HorizontalScroll(int scrollAmountInClicks)
    {
        if (TryUseHardware(backend =>
        {
            backend.MouseWheelHorizontal(scrollAmountInClicks);
            return true;
        }))
        {
            return this;
        }

        Virtual.HorizontalScroll(scrollAmountInClicks);
        return this;
    }

    public IMouseSimulator Sleep(int millsecondsTimeout)
    {
        Thread.Sleep(millsecondsTimeout);
        return this;
    }

    public IMouseSimulator Sleep(TimeSpan timeout)
    {
        Thread.Sleep(timeout);
        return this;
    }

    private MouseSimulator Virtual => _virtualMouse ?? throw new InvalidOperationException("Routing mouse simulator is not initialized.");

    private IMouseSimulator MouseButtonDown(HardwareMouseButton button, Func<MouseSimulator, IMouseSimulator> fallback)
    {
        if (TryUseHardware(backend =>
        {
            backend.MouseButtonDown(button);
            return true;
        }))
        {
            return this;
        }

        fallback(Virtual);
        return this;
    }

    private IMouseSimulator MouseButtonUp(HardwareMouseButton button, Func<MouseSimulator, IMouseSimulator> fallback)
    {
        if (TryUseHardware(backend =>
        {
            backend.MouseButtonUp(button);
            return true;
        }))
        {
            return this;
        }

        fallback(Virtual);
        return this;
    }

    private IMouseSimulator MouseButtonClick(HardwareMouseButton button, int count, Func<MouseSimulator, IMouseSimulator> fallback)
    {
        if (TryUseHardware(backend =>
        {
            backend.MouseButtonClick(button, count);
            return true;
        }))
        {
            return this;
        }

        fallback(Virtual);
        return this;
    }

    private static HardwareMouseButton ToXButton(int buttonId)
    {
        return buttonId == 0x0002 ? HardwareMouseButton.Side2 : HardwareMouseButton.Side1;
    }

    private static int ToScreenX(double absoluteX)
    {
        return (int)Math.Round(Math.Clamp(absoluteX, 0, 65535) * PrimaryScreen.WorkingArea.Width / 65535d);
    }

    private static int ToScreenY(double absoluteY)
    {
        return (int)Math.Round(Math.Clamp(absoluteY, 0, 65535) * PrimaryScreen.WorkingArea.Height / 65535d);
    }

    private static bool TryUseHardware(Func<IHardwareMouseBackend, bool> action)
    {
        var backend = HardwareInputRouter.Instance.GetMouseBackend();
        return backend != null && action(backend);
    }
}
