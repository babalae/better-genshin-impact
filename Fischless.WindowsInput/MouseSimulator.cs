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

    private void SendSimulatedInput(User32.INPUT[] inputList)
    {
        _messageDispatcher.DispatchInput(inputList);
    }

    public IMouseSimulator MoveMouseBy(int pixelDeltaX, int pixelDeltaY)
    {
        User32.INPUT[] inputList = new InputBuilder().AddRelativeMouseMovement(pixelDeltaX, pixelDeltaY).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IMouseSimulator MoveMouseTo(double absoluteX, double absoluteY)
    {
        User32.INPUT[] inputList = new InputBuilder().AddAbsoluteMouseMovement((int)Math.Truncate(absoluteX), (int)Math.Truncate(absoluteY)).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IMouseSimulator MoveMouseToPositionOnVirtualDesktop(double absoluteX, double absoluteY)
    {
        User32.INPUT[] inputList = new InputBuilder().AddAbsoluteMouseMovementOnVirtualDesktop((int)Math.Truncate(absoluteX), (int)Math.Truncate(absoluteY)).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IMouseSimulator LeftButtonDown()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonDown(MouseButton.LeftButton).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IMouseSimulator LeftButtonUp()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonUp(MouseButton.LeftButton).ToArray();
        this.SendSimulatedInput(inputList);
        return this;
    }

    public IMouseSimulator LeftButtonClick()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonClick(MouseButton.LeftButton).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IMouseSimulator LeftButtonDoubleClick()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonDoubleClick(MouseButton.LeftButton).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IMouseSimulator MiddleButtonDown()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonDown(MouseButton.MiddleButton).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IMouseSimulator MiddleButtonUp()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonUp(MouseButton.MiddleButton).ToArray();
        this.SendSimulatedInput(inputList);
        return this;
    }

    public IMouseSimulator MiddleButtonClick()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonClick(MouseButton.MiddleButton).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IMouseSimulator MiddleButtonDoubleClick()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonDoubleClick(MouseButton.MiddleButton).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IMouseSimulator RightButtonDown()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonDown(MouseButton.RightButton).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IMouseSimulator RightButtonUp()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonUp(MouseButton.RightButton).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IMouseSimulator RightButtonClick()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonClick(MouseButton.RightButton).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IMouseSimulator RightButtonDoubleClick()
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseButtonDoubleClick(MouseButton.RightButton).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IMouseSimulator XButtonDown(int buttonId)
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseXButtonDown(buttonId).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IMouseSimulator XButtonUp(int buttonId)
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseXButtonUp(buttonId).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IMouseSimulator XButtonClick(int buttonId)
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseXButtonClick(buttonId).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IMouseSimulator XButtonDoubleClick(int buttonId)
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseXButtonDoubleClick(buttonId).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IMouseSimulator VerticalScroll(int scrollAmountInClicks)
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseVerticalWheelScroll(scrollAmountInClicks * 120).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IMouseSimulator HorizontalScroll(int scrollAmountInClicks)
    {
        User32.INPUT[] inputList = new InputBuilder().AddMouseHorizontalWheelScroll(scrollAmountInClicks * 120).ToArray();
        SendSimulatedInput(inputList);
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
