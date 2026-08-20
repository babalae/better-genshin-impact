using Vanara.PInvoke;

namespace Fischless.WindowsInput;

public class MouseSimulator : IMouseSimulator
{
    public MouseSimulator(IInputSimulator inputSimulator)
    {
        _inputSimulator = inputSimulator ?? throw new ArgumentNullException(nameof(inputSimulator));
        _messageDispatcher = new WindowsInputMessageDispatcher();
    }

    internal MouseSimulator(IInputSimulator inputSimulator, IInputMessageDispatcher messageDispatcher)
    {
        _inputSimulator = inputSimulator ?? throw new ArgumentNullException(nameof(inputSimulator));
        _messageDispatcher = messageDispatcher ?? throw new InvalidOperationException(string.Format("The {0} cannot operate with a null {1}. Please provide a valid {1} instance to use for dispatching {2} messages.", nameof(MouseSimulator), typeof(IInputMessageDispatcher).Name, typeof(User32.INPUT).Name));
    }

    public IKeyboardSimulator Keyboard => _inputSimulator.Keyboard;

    private void SendSimulatedInput(User32.INPUT[] inputList, string action, string detail = "")
    {
        // 先发输入再埋点，避免拖地图等热路径被同步 I/O 打断
        _messageDispatcher.DispatchInput(inputList);
        InputDebugHook.Record(action, detail);
    }

    public IMouseSimulator MoveMouseBy(int pixelDeltaX, int pixelDeltaY)
    {
        User32.INPUT[] inputList = new InputBuilder().AddRelativeMouseMovement(pixelDeltaX, pixelDeltaY).ToArray();
        SendSimulatedInput(inputList, "Mouse.MoveBy", $"dx={pixelDeltaX};dy={pixelDeltaY}");
        return this;
    }

    public IMouseSimulator MoveMouseTo(double absoluteX, double absoluteY)
    {
        User32.INPUT[] inputList = new InputBuilder().AddAbsoluteMouseMovement((int)Math.Truncate(absoluteX), (int)Math.Truncate(absoluteY)).ToArray();
        SendSimulatedInput(inputList, "Mouse.MoveTo", $"x={absoluteX:0.##};y={absoluteY:0.##}");
        return this;
    }

    public IMouseSimulator MoveMouseToPositionOnVirtualDesktop(double absoluteX, double absoluteY)
    {
        User32.INPUT[] inputList = new InputBuilder().AddAbsoluteMouseMovementOnVirtualDesktop((int)Math.Truncate(absoluteX), (int)Math.Truncate(absoluteY)).ToArray();
        SendSimulatedInput(inputList, "Mouse.MoveToVirtualDesktop", $"x={absoluteX:0.##};y={absoluteY:0.##}");
        return this;
    }

    public IMouseSimulator LeftButtonDown()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonDown(MouseButton.LeftButton).ToArray();
        SendSimulatedInput(inputList, "Mouse.LeftDown");
        return this;
    }

    public IMouseSimulator LeftButtonUp()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonUp(MouseButton.LeftButton).ToArray();
        SendSimulatedInput(inputList, "Mouse.LeftUp");
        return this;
    }

    public IMouseSimulator LeftButtonClick()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonClick(MouseButton.LeftButton).ToArray();
        SendSimulatedInput(inputList, "Mouse.LeftClick");
        return this;
    }

    public IMouseSimulator LeftButtonDoubleClick()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonDoubleClick(MouseButton.LeftButton).ToArray();
        SendSimulatedInput(inputList, "Mouse.LeftDoubleClick");
        return this;
    }

    public IMouseSimulator MiddleButtonDown()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonDown(MouseButton.MiddleButton).ToArray();
        SendSimulatedInput(inputList, "Mouse.MiddleDown");
        return this;
    }

    public IMouseSimulator MiddleButtonUp()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonUp(MouseButton.MiddleButton).ToArray();
        SendSimulatedInput(inputList, "Mouse.MiddleUp");
        return this;
    }

    public IMouseSimulator MiddleButtonClick()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonClick(MouseButton.MiddleButton).ToArray();
        SendSimulatedInput(inputList, "Mouse.MiddleClick");
        return this;
    }

    public IMouseSimulator MiddleButtonDoubleClick()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonDoubleClick(MouseButton.MiddleButton).ToArray();
        SendSimulatedInput(inputList, "Mouse.MiddleDoubleClick");
        return this;
    }

    public IMouseSimulator RightButtonDown()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonDown(MouseButton.RightButton).ToArray();
        SendSimulatedInput(inputList, "Mouse.RightDown");
        return this;
    }

    public IMouseSimulator RightButtonUp()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonUp(MouseButton.RightButton).ToArray();
        SendSimulatedInput(inputList, "Mouse.RightUp");
        return this;
    }

    public IMouseSimulator RightButtonClick()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonClick(MouseButton.RightButton).ToArray();
        SendSimulatedInput(inputList, "Mouse.RightClick");
        return this;
    }

    public IMouseSimulator RightButtonDoubleClick()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonDoubleClick(MouseButton.RightButton).ToArray();
        SendSimulatedInput(inputList, "Mouse.RightDoubleClick");
        return this;
    }

    public IMouseSimulator XButtonDown(int buttonId)
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseXButtonDown(buttonId).ToArray();
        SendSimulatedInput(inputList, "Mouse.XDown", $"id={buttonId}");
        return this;
    }

    public IMouseSimulator XButtonUp(int buttonId)
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseXButtonUp(buttonId).ToArray();
        SendSimulatedInput(inputList, "Mouse.XUp", $"id={buttonId}");
        return this;
    }

    public IMouseSimulator XButtonClick(int buttonId)
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseXButtonClick(buttonId).ToArray();
        SendSimulatedInput(inputList, "Mouse.XClick", $"id={buttonId}");
        return this;
    }

    public IMouseSimulator XButtonDoubleClick(int buttonId)
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseXButtonDoubleClick(buttonId).ToArray();
        SendSimulatedInput(inputList, "Mouse.XDoubleClick", $"id={buttonId}");
        return this;
    }

    public IMouseSimulator VerticalScroll(int scrollAmountInClicks)
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseVerticalWheelScroll(scrollAmountInClicks * 120).ToArray();
        SendSimulatedInput(inputList, "Mouse.VerticalScroll", $"clicks={scrollAmountInClicks}");
        return this;
    }

    public IMouseSimulator HorizontalScroll(int scrollAmountInClicks)
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseHorizontalWheelScroll(scrollAmountInClicks * 120).ToArray();
        SendSimulatedInput(inputList, "Mouse.HorizontalScroll", $"clicks={scrollAmountInClicks}");
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

    private readonly IInputSimulator _inputSimulator;

    private readonly IInputMessageDispatcher _messageDispatcher;
}
