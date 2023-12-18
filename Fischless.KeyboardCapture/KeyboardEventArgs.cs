namespace Fischless.KeyboardCapture;

public delegate void KeyboardEventHandler(object? sender, KeyboardEventArgs e);

public delegate void KeyboardPressEventHandler(object? sender, KeyboardPressEventArgs e);

public class KeyboardEventArgs
{
    private bool _suppressKeyPress;

    public KeyboardEventArgs(KeyboardKeys keyData)
    {
        KeyData = keyData;
    }

    public virtual bool Alt => (KeyData & KeyboardKeys.Alt) == KeyboardKeys.Alt;

    public bool Control => (KeyData & KeyboardKeys.Control) == KeyboardKeys.Control;

    public bool Handled { get; set; }

    public KeyboardKeys KeyCode
    {
        get
        {
            KeyboardKeys keyGenerated = KeyData & KeyboardKeys.KeyCode;

            // since Keys can be discontiguous, keeping Enum.IsDefined.
            if (!Enum.IsDefined(typeof(KeyboardKeys), (int)keyGenerated))
            {
                return KeyboardKeys.None;
            }

            return keyGenerated;
        }
    }

    public int KeyValue => (int)(KeyData & KeyboardKeys.KeyCode);

    public KeyboardKeys KeyData { get; }

    public KeyboardKeys Modifiers => KeyData & KeyboardKeys.Modifiers;

    public virtual bool Shift => (KeyData & KeyboardKeys.Shift) == KeyboardKeys.Shift;

    public bool SuppressKeyPress
    {
        get => _suppressKeyPress;
        set
        {
            _suppressKeyPress = value;
            Handled = value;
        }
    }
}

public class KeyboardPressEventArgs : EventArgs
{
    public KeyboardPressEventArgs(char keyChar)
    {
        KeyChar = keyChar;
    }

    public char KeyChar { get; set; }
    public bool Handled { get; set; }
}
