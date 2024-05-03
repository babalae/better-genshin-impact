namespace Fischless.WindowsInput;

public interface IMouseSimulator
{
    public IKeyboardSimulator Keyboard { get; }

    public IMouseSimulator MoveMouseBy(int pixelDeltaX, int pixelDeltaY);

    public IMouseSimulator MoveMouseTo(double absoluteX, double absoluteY);

    public IMouseSimulator MoveMouseToPositionOnVirtualDesktop(double absoluteX, double absoluteY);

    public IMouseSimulator LeftButtonDown();

    public IMouseSimulator LeftButtonUp();

    public IMouseSimulator LeftButtonClick();

    public IMouseSimulator LeftButtonDoubleClick();

    public IMouseSimulator MiddleButtonDown();

    public IMouseSimulator MiddleButtonUp();

    public IMouseSimulator MiddleButtonClick();

    public IMouseSimulator MiddleButtonDoubleClick();

    public IMouseSimulator RightButtonDown();

    public IMouseSimulator RightButtonUp();

    public IMouseSimulator RightButtonClick();

    public IMouseSimulator RightButtonDoubleClick();

    public IMouseSimulator XButtonDown(int buttonId);

    public IMouseSimulator XButtonUp(int buttonId);

    public IMouseSimulator XButtonClick(int buttonId);

    public IMouseSimulator XButtonDoubleClick(int buttonId);

    public IMouseSimulator VerticalScroll(int scrollAmountInClicks);

    public IMouseSimulator HorizontalScroll(int scrollAmountInClicks);

    public IMouseSimulator Sleep(int millsecondsTimeout);

    public IMouseSimulator Sleep(TimeSpan timeout);
}
