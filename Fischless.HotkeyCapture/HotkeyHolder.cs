namespace Fischless.HotkeyCapture;

public sealed class HotkeyHolder
{
    private static Hotkey? hotkey;
    private static HotkeyHook? hotkeyHook;
    private static Action<object?, KeyPressedEventArgs>? keyPressed;

    public static void RegisterHotKey(string hotkeyStr, Action<object?, KeyPressedEventArgs> keyPressed = null!)
    {
        if (string.IsNullOrEmpty(hotkeyStr))
        {
            UnregisterHotKey();
            return;
        }

        hotkey = new Hotkey(hotkeyStr);

        hotkeyHook?.Dispose();
        hotkeyHook = new HotkeyHook();
        hotkeyHook.KeyPressed -= OnKeyPressed;
        hotkeyHook.KeyPressed += OnKeyPressed;
        HotkeyHolder.keyPressed = keyPressed;
        hotkeyHook.RegisterHotKey(hotkey.ModifierKey, hotkey.Key);
    }

    public static void UnregisterHotKey()
    {
        if (hotkeyHook != null)
        {
            hotkeyHook.KeyPressed -= OnKeyPressed;
            hotkeyHook.UnregisterHotKey();
            hotkeyHook.Dispose();
        }
    }

    private static void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        keyPressed?.Invoke(sender, e);
    }
}
