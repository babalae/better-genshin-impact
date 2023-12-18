using Vanara.PInvoke;

namespace Fischless.KeyboardCapture;

public static class KeyboardExtension
{
    public static bool IsKeyLocked(this KeyboardKeys keyVal)
    {
        if (keyVal == KeyboardKeys.Insert || keyVal == KeyboardKeys.NumLock || keyVal == KeyboardKeys.CapsLock || keyVal == KeyboardKeys.Scroll)
        {
            int result = User32.GetKeyState((int)keyVal);

            // If the high-order bit is 1, the key is down; otherwise, it is up.
            // If the low-order bit is 1, the key is toggled. A key, such as the CAPS LOCK key,
            // is toggled if it is turned on. The key is off and untoggled if the low-order bit is 0.
            // A toggle key's indicator light (if any) on the keyboard will be on when the key is toggled,
            // and off when the key is untoggled.

            // Toggle keys (only low bit is of interest).
            if (keyVal == KeyboardKeys.Insert || keyVal == KeyboardKeys.CapsLock)
            {
                return (result & 0x1) != 0x0;
            }

            return (result & 0x8001) != 0x0;
        }

        // else - it's an un-lockable key.
        // Actually get the exception string from the system resource.
        throw new NotSupportedException("Specified key is not supported.");
    }

    public static bool IsExtendedKey(this KeyboardKeys keyVal)
    {
        switch (keyVal)
        {
            case KeyboardKeys.Return:
                short state = User32.GetKeyState((int)KeyboardKeys.Return);
                return state == 0x01 || state == -128;
        }
        return false;
    }

    public static bool IsCombinationKey(this KeyboardKeys keyVal)
    {
        return keyVal switch
        {
            KeyboardKeys.Shift
            or KeyboardKeys.ShiftKey
            or KeyboardKeys.LShiftKey
            or KeyboardKeys.RShiftKey
            or KeyboardKeys.Control
            or KeyboardKeys.ControlKey
            or KeyboardKeys.LControlKey
            or KeyboardKeys.RControlKey
            or KeyboardKeys.LWin
            or KeyboardKeys.RWin
            or KeyboardKeys.Alt
            or KeyboardKeys.LMenu
            or KeyboardKeys.RMenu => true,
            _ => false,
        };
    }

    public static string ToName(this KeyboardKeys keyVal, bool useStrict = false)
    {
        if (useStrict)
        {
            throw new NotImplementedException(nameof(useStrict));
        }

        return keyVal switch
        {
            KeyboardKeys.Control => KeyboardConst.Ctrl,
            KeyboardKeys.Return => KeyboardConst.Enter,
            KeyboardKeys.NumEnter => KeyboardConst.NumEnter,
            KeyboardKeys.ShiftKey => KeyboardConst.Shift,
            KeyboardKeys.ControlKey => KeyboardConst.Ctrl,
            KeyboardKeys.Menu => KeyboardConst.Alt,
            KeyboardKeys.Capital => KeyboardConst.CapsLock,
            KeyboardKeys.Escape => KeyboardConst.Esc,
            KeyboardKeys.PageUp => KeyboardConst.PgUp,
            KeyboardKeys.PageDown => KeyboardConst.PgDn,
            KeyboardKeys.Delete => KeyboardConst.Del,
            KeyboardKeys.D0 => 0.ToString(),
            KeyboardKeys.D1 => 1.ToString(),
            KeyboardKeys.D2 => 2.ToString(),
            KeyboardKeys.D3 => 3.ToString(),
            KeyboardKeys.D4 => 4.ToString(),
            KeyboardKeys.D5 => 5.ToString(),
            KeyboardKeys.D6 => 6.ToString(),
            KeyboardKeys.D7 => 7.ToString(),
            KeyboardKeys.D8 => 8.ToString(),
            KeyboardKeys.D9 => 9.ToString(),
            KeyboardKeys.LWin => KeyboardConst.Win,
            KeyboardKeys.RWin => KeyboardConst.Win,
            KeyboardKeys.NumPad0 => KeyboardConst.Num + 0,
            KeyboardKeys.NumPad1 => KeyboardConst.Num + 1,
            KeyboardKeys.NumPad2 => KeyboardConst.Num + 2,
            KeyboardKeys.NumPad3 => KeyboardConst.Num + 3,
            KeyboardKeys.NumPad4 => KeyboardConst.Num + 4,
            KeyboardKeys.NumPad5 => KeyboardConst.Num + 5,
            KeyboardKeys.NumPad6 => KeyboardConst.Num + 6,
            KeyboardKeys.NumPad7 => KeyboardConst.Num + 7,
            KeyboardKeys.NumPad8 => KeyboardConst.Num + 8,
            KeyboardKeys.NumPad9 => KeyboardConst.Num + 9,
            KeyboardKeys.Multiply => KeyboardConst.NumpadAsterisk,
            KeyboardKeys.Add => KeyboardConst.NumpadPlus,
            KeyboardKeys.Subtract => KeyboardConst.NumpadMinus,
            KeyboardKeys.Decimal => KeyboardConst.NumpadDot,
            KeyboardKeys.Divide => KeyboardConst.NumpadDot,
            KeyboardKeys.Scroll => KeyboardConst.ScrollLock,
            KeyboardKeys.LShiftKey or KeyboardKeys.RShiftKey => KeyboardConst.Shift,
            KeyboardKeys.LControlKey or KeyboardKeys.RControlKey => KeyboardConst.Ctrl,
            KeyboardKeys.LMenu or KeyboardKeys.RMenu => KeyboardConst.Alt,
            KeyboardKeys.OemSemicolon => KeyboardConst.Semicolon,
            KeyboardKeys.Oemplus => KeyboardConst.Equal,
            KeyboardKeys.Oemcomma => KeyboardConst.Comma,
            KeyboardKeys.OemMinus => KeyboardConst.Minus,
            KeyboardKeys.OemPeriod => KeyboardConst.Period,
            KeyboardKeys.OemQuestion => KeyboardConst.Question,
            KeyboardKeys.Oemtilde => KeyboardConst.Tilde,
            KeyboardKeys.OemOpenBrackets => KeyboardConst.LeftSquareBracket,
            KeyboardKeys.OemPipe => KeyboardConst.Pipe,
            KeyboardKeys.OemCloseBrackets => KeyboardConst.RightSquareBracket,
            KeyboardKeys.OemQuotes => KeyboardConst.Apostrophe,
            KeyboardKeys.OemBackslash => KeyboardConst.Backslash,
            KeyboardKeys.Shift => KeyboardConst.Shift,
            KeyboardKeys.Alt => KeyboardConst.Alt,
            _ => keyVal.ToString(),
        };
    }
}

public enum KeyboardNameType
{
    Default,
    Strict,
}
