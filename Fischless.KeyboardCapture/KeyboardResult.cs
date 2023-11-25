namespace Fischless.KeyboardCapture;

public sealed class KeyboardResult
{
    public KeyboardItem KeyboardItem { get; init; } = default;
    public string Key => KeyboardItem.Key ?? KeyboardItem.KeyCode.ToString();
    public bool IsShift { get; set; } = false;
    public bool IsCtrl { get; set; } = false;
    public bool IsAlt { get; set; } = false;
    public bool IsWin { get; set; } = false;

    public KeyboardResult(KeyboardItem keyboardItem)
    {
        KeyboardItem = keyboardItem;
    }

    public override string ToString()
    {
        List<string> keyModifiers = new();

        if (IsCtrl)
        {
            keyModifiers.Add("Ctrl");
        }

        if (IsShift)
        {
            keyModifiers.Add("Shift");
        }

        if (IsAlt)
        {
            keyModifiers.Add("Alt");
        }

        string keyModifiersStr = string.Join("+", keyModifiers);

        if (!string.IsNullOrEmpty(keyModifiersStr) && !string.IsNullOrEmpty(Key))
        {
            return $"{keyModifiersStr}+{Key}";
        }
        else
        {
            return Key;
        }
    }

    public static implicit operator string(KeyboardResult result) => result?.ToString();
}
