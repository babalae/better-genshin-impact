namespace Fischless.KeyboardCapture;

public record struct KeyboardItem
{
    public DateTime DateTime;
    public KeyboardKeys KeyCode;
    public string Key;

    public KeyboardItem(DateTime dateTime, KeyboardKeys keyCode, string? key = null)
    {
        DateTime = dateTime;
        KeyCode = keyCode;
        Key = key;
    }
}
