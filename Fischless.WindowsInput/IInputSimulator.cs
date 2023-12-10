namespace Fischless.WindowsInput;

public interface IInputSimulator
{
    public IKeyboardSimulator Keyboard { get; }
    public IMouseSimulator Mouse { get; }
    public IInputDeviceStateAdaptor InputDeviceState { get; }
}
