namespace Fischless.KeyboardCapture;

public sealed class KeyboardResult
{
    public KeyboardItem KeyboardItem { get; init; } = default;
    public string Key => KeyboardItem.Key ?? KeyboardItem.KeyCode.ToName();
    public bool IsShift { get; set; } = false;
    public bool IsCtrl { get; set; } = false;
    public bool IsAlt { get; set; } = false;
    public bool IsWin { get; set; } = false;
    public bool IsExtendedKey { get; set; } = false;

    public KeyboardResult(KeyboardItem keyboardItem)
    {
        KeyboardItem = keyboardItem;
    }

    public override string ToString()
    {
        List<string> keyModifiers = [];

        if (IsCtrl)
        {
            return KeyboardConst.Ctrl;
        }

        if (IsShift)
        {
            return KeyboardConst.Shift;
        }

        if (IsAlt)
        {
            return KeyboardConst.Alt;
        }

        if (IsWin)
        {
            return KeyboardConst.Win;
        }

        if (keyModifiers.Count > 0)
        {
            if (KeyboardItem.KeyCode.IsCombinationKey())
            {
                if (IsCtrl)
                {
                    return KeyboardConst.Ctrl;
                }

                if (IsShift)
                {
                    return KeyboardConst.Shift;
                }

                if (IsAlt)
                {
                    return KeyboardConst.Alt;
                }

                if (IsWin)
                {
                    return KeyboardConst.Win;
                }
            }
            else
            {
                return $"{string.Join('+', keyModifiers)}+{Key}";
            }
        }
        return Key;
    }

    public static implicit operator string(KeyboardResult result) => result?.ToString();
}
