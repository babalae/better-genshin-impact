namespace Fischless.KeyboardCapture;

public record struct KeyboardItem
{
    public DateTime DateTime;
    public Keys KeyCode;
    public string Key;

    public KeyboardItem(DateTime dateTime, Keys keyCode, string? key = null)
    {
        DateTime = dateTime;
        KeyCode = keyCode;
        Key = key;
    }
}
