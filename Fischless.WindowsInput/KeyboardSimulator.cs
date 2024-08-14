using Vanara.PInvoke;

namespace Fischless.WindowsInput;

public class KeyboardSimulator : IKeyboardSimulator
{
    public KeyboardSimulator(IInputSimulator inputSimulator)
    {
        _inputSimulator = inputSimulator ?? throw new ArgumentNullException(nameof(inputSimulator));
        _messageDispatcher = new WindowsInputMessageDispatcher();
    }

    internal KeyboardSimulator(IInputSimulator inputSimulator, IInputMessageDispatcher messageDispatcher)
    {
        _inputSimulator = inputSimulator ?? throw new ArgumentNullException(nameof(inputSimulator));
        _messageDispatcher = messageDispatcher ?? throw new InvalidOperationException(string.Format("The {0} cannot operate with a null {1}. Please provide a valid {1} instance to use for dispatching {2} messages.", typeof(KeyboardSimulator).Name, typeof(IInputMessageDispatcher).Name, typeof(User32.INPUT).Name));
    }

    public IMouseSimulator Mouse => _inputSimulator.Mouse;

    private void ModifiersDown(InputBuilder builder, IEnumerable<User32.VK> modifierKeyCodes)
    {
        if (modifierKeyCodes == null)
        {
            return;
        }
        foreach (User32.VK keyCode in modifierKeyCodes)
        {
            builder.AddKeyDown(keyCode);
        }
    }

    private void ModifiersUp(InputBuilder builder, IEnumerable<User32.VK> modifierKeyCodes)
    {
        if (modifierKeyCodes == null)
        {
            return;
        }
        Stack<User32.VK> stack = new(modifierKeyCodes);
        while (stack.Count > 0)
        {
            builder.AddKeyUp(stack.Pop());
        }
    }

    private void KeysPress(InputBuilder builder, IEnumerable<User32.VK> keyCodes, bool? isExtendedKey = null)
    {
        if (keyCodes == null)
        {
            return;
        }
        foreach (User32.VK keyCode in keyCodes)
        {
            builder.AddKeyPress(keyCode, isExtendedKey);
        }
    }

    private void SendSimulatedInput(User32.INPUT[] inputList)
    {
        _messageDispatcher.DispatchInput(inputList);
    }

    public IKeyboardSimulator KeyDown(User32.VK keyCode)
    {
        User32.INPUT[] inputList = new InputBuilder().AddKeyDown(keyCode).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IKeyboardSimulator KeyDown(bool? isExtendedKey, User32.VK keyCode)
    {
        User32.INPUT[] inputList = new InputBuilder().AddKeyDown(keyCode, isExtendedKey).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IKeyboardSimulator KeyUp(User32.VK keyCode)
    {
        User32.INPUT[] inputList = new InputBuilder().AddKeyUp(keyCode).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IKeyboardSimulator KeyUp(bool? isExtendedKey, User32.VK keyCode)
    {
        User32.INPUT[] inputList = new InputBuilder().AddKeyUp(keyCode, isExtendedKey).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IKeyboardSimulator KeyPress(User32.VK keyCode)
    {
        User32.INPUT[] inputList = new InputBuilder().AddKeyPress(keyCode).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IKeyboardSimulator KeyPress(bool? isExtendedKey, User32.VK keyCode)
    {
        User32.INPUT[] inputList = new InputBuilder().AddKeyPress(keyCode, isExtendedKey).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IKeyboardSimulator KeyPress(params User32.VK[] keyCodes)
    {
        InputBuilder inputBuilder = new();
        KeysPress(inputBuilder, keyCodes);
        SendSimulatedInput(inputBuilder.ToArray());
        return this;
    }

    public IKeyboardSimulator KeyPress(bool? isExtendedKey, params User32.VK[] keyCodes)
    {
        InputBuilder inputBuilder = new();
        KeysPress(inputBuilder, keyCodes, isExtendedKey);
        SendSimulatedInput(inputBuilder.ToArray());
        return this;
    }

    public IKeyboardSimulator ModifiedKeyStroke(User32.VK modifierKeyCode, User32.VK keyCode)
    {
        ModifiedKeyStroke(
        [
            modifierKeyCode,
        ],
        [
            keyCode,
        ]);
        return this;
    }

    public IKeyboardSimulator ModifiedKeyStroke(IEnumerable<User32.VK> modifierKeyCodes, User32.VK keyCode)
    {
        ModifiedKeyStroke(modifierKeyCodes,
        [
            keyCode
        ]);
        return this;
    }

    public IKeyboardSimulator ModifiedKeyStroke(User32.VK modifierKey, IEnumerable<User32.VK> keyCodes)
    {
        ModifiedKeyStroke(
        [
            modifierKey
        ], keyCodes);
        return this;
    }

    public IKeyboardSimulator ModifiedKeyStroke(IEnumerable<User32.VK> modifierKeyCodes, IEnumerable<User32.VK> keyCodes)
    {
        InputBuilder inputBuilder = new();
        ModifiersDown(inputBuilder, modifierKeyCodes);
        KeysPress(inputBuilder, keyCodes);
        ModifiersUp(inputBuilder, modifierKeyCodes);
        SendSimulatedInput(inputBuilder.ToArray());
        return this;
    }

    public IKeyboardSimulator TextEntry(string text)
    {
        if (text.Length > 2147483647L)
        {
            throw new ArgumentException(string.Format("The text parameter is too long. It must be less than {0} characters.", 2147483647U), "text");
        }
        User32.INPUT[] inputList = new InputBuilder().AddCharacters(text).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IKeyboardSimulator TextEntry(char character)
    {
        User32.INPUT[] inputList = new InputBuilder().AddCharacter(character).ToArray();
        SendSimulatedInput(inputList);
        return this;
    }

    public IKeyboardSimulator Sleep(int millsecondsTimeout)
    {
        Thread.Sleep(millsecondsTimeout);
        return this;
    }

    public IKeyboardSimulator Sleep(TimeSpan timeout)
    {
        Thread.Sleep(timeout);
        return this;
    }

    private readonly IInputSimulator _inputSimulator;

    private readonly IInputMessageDispatcher _messageDispatcher;
}
