using Vanara.PInvoke;

namespace Fischless.WindowsInput;

public interface IKeyboardSimulator
{
    public IMouseSimulator Mouse { get; }

    public IKeyboardSimulator KeyDown(User32.VK keyCode);

    public IKeyboardSimulator KeyDown(bool? isExtendedKey, User32.VK keyCode);

    public IKeyboardSimulator KeyPress(User32.VK keyCode);

    public IKeyboardSimulator KeyPress(bool? isExtendedKey, User32.VK keyCode);

    public IKeyboardSimulator KeyPress(params User32.VK[] keyCodes);

    public IKeyboardSimulator KeyPress(bool? isExtendedKey, params User32.VK[] keyCodes);

    public IKeyboardSimulator KeyUp(User32.VK keyCode);

    public IKeyboardSimulator KeyUp(bool? isExtendedKey, User32.VK keyCode);

    public IKeyboardSimulator ModifiedKeyStroke(IEnumerable<User32.VK> modifierKeyCodes, IEnumerable<User32.VK> keyCodes);

    public IKeyboardSimulator ModifiedKeyStroke(IEnumerable<User32.VK> modifierKeyCodes, User32.VK keyCode);

    public IKeyboardSimulator ModifiedKeyStroke(User32.VK modifierKey, IEnumerable<User32.VK> keyCodes);

    public IKeyboardSimulator ModifiedKeyStroke(User32.VK modifierKeyCode, User32.VK keyCode);

    public IKeyboardSimulator TextEntry(string text);

    public IKeyboardSimulator TextEntry(char character);

    public IKeyboardSimulator Sleep(int millsecondsTimeout);

    public IKeyboardSimulator Sleep(TimeSpan timeout);
}
