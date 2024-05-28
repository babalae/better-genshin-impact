using System.Diagnostics;

namespace Fischless.KeyboardCapture;

[DebuggerDisplay("{result.ToString()}")]
public class KeyboardReader : IDisposable
{
    public static KeyboardReader Default { get; } = new();

    public event EventHandler<KeyboardResult> Received = null!;

    public bool IsCombinationOnly { get; set; } = false;
    public bool IsCaseSensitived { get; set; } = false;
    public bool IsShift { get; private set; } = false;
    public bool IsCtrl { get; private set; } = false;
    public bool IsAlt { get; private set; } = false;
    public bool IsWin { get; private set; } = false;
    public bool IsExtendedKey { get; private set; } = false;

    protected KeyboardHook KeyboardHook = new();

    public KeyboardReader()
    {
        Start();
    }

    ~KeyboardReader()
    {
        Dispose();
    }

    public void Dispose()
    {
        Stop();
    }

    public void Start()
    {
        KeyboardHook.KeyDown -= OnKeyDown;
        KeyboardHook.KeyDown += OnKeyDown;
        KeyboardHook.KeyUp -= OnKeyUp;
        KeyboardHook.KeyUp += OnKeyUp;
        KeyboardHook.Start();
    }

    public void Stop()
    {
        KeyboardHook.Stop();
        KeyboardHook.KeyDown -= OnKeyDown;
        KeyboardHook.KeyUp -= OnKeyUp;
    }

    private void OnKeyDown(object? sender, KeyboardEventArgs e)
    {
        if (e.KeyCode == KeyboardKeys.Shift
         || e.KeyCode == KeyboardKeys.ShiftKey
         || e.KeyCode == KeyboardKeys.LShiftKey
         || e.KeyCode == KeyboardKeys.RShiftKey)
        {
            if (!IsShift)
            {
                IsShift = true;
                IsExtendedKey = e.KeyCode == KeyboardKeys.RShiftKey;
                if (IsCombinationOnly)
                {
                    return;
                }
            }
        }
        else if (e.KeyCode == KeyboardKeys.Control
              || e.KeyCode == KeyboardKeys.ControlKey
              || e.KeyCode == KeyboardKeys.LControlKey
              || e.KeyCode == KeyboardKeys.RControlKey)
        {
            if (!IsCtrl)
            {
                IsCtrl = true;
                IsExtendedKey = e.KeyCode == KeyboardKeys.RControlKey;
                if (IsCombinationOnly)
                {
                    return;
                }
            }
        }
        else if (e.KeyCode == KeyboardKeys.LWin
              || e.KeyCode == KeyboardKeys.RWin)
        {
            if (!IsWin)
            {
                IsWin = true;
                IsExtendedKey = e.KeyCode == KeyboardKeys.RWin;
                if (IsCombinationOnly)
                {
                    return;
                }
            }
        }
        else if (e.KeyCode == KeyboardKeys.Alt
              || e.KeyCode == KeyboardKeys.LMenu
              || e.KeyCode == KeyboardKeys.RMenu)
        {
            if (!IsAlt)
            {
                IsAlt = true;
                IsExtendedKey = e.KeyCode == KeyboardKeys.RMenu;
                if (IsCombinationOnly)
                {
                    return;
                }
            }
        }
        else if (e.KeyCode == KeyboardKeys.Return)
        {
            IsExtendedKey = KeyboardKeys.Return.IsExtendedKey();
        }

        DateTime now = DateTime.Now;

#if false
        Debug.WriteLine(e.KeyCode);
#endif

        KeyboardItem item;

        if (IsCaseSensitived)
        {
            bool isUpper = KeyboardKeys.CapsLock.IsKeyLocked() ? !IsShift : IsShift;

            if (isUpper)
            {
                item = new KeyboardItem(now, e.KeyCode, char.ToUpper((char)e.KeyCode).ToString());
            }
            else
            {
                item = new KeyboardItem(now, e.KeyCode, char.ToLower((char)e.KeyCode).ToString());
            }
        }
        else
        {
            item = new KeyboardItem(now, e.KeyCode);
        }

        KeyboardResult result = new(item)
        {
            IsShift = IsShift,
            IsCtrl = IsCtrl,
            IsAlt = IsAlt,
            IsWin = IsWin,
            IsExtendedKey = IsExtendedKey,
        };

        Received?.Invoke(this, result);
#if true
        Debug.WriteLine(result.ToString());
#endif
    }

    private void OnKeyUp(object? sender, KeyboardEventArgs e)
    {
        if (e.KeyCode == KeyboardKeys.Shift
         || e.KeyCode == KeyboardKeys.ShiftKey
         || e.KeyCode == KeyboardKeys.LShiftKey
         || e.KeyCode == KeyboardKeys.RShiftKey)
        {
            IsShift = false;
            IsExtendedKey = e.KeyCode == KeyboardKeys.RShiftKey;
            return;
        }
        else if (e.KeyCode == KeyboardKeys.Control
              || e.KeyCode == KeyboardKeys.ControlKey
              || e.KeyCode == KeyboardKeys.LControlKey
              || e.KeyCode == KeyboardKeys.RControlKey)
        {
            IsExtendedKey = e.KeyCode == KeyboardKeys.RControlKey;
            IsCtrl = false;
            return;
        }
        else if (e.KeyCode == KeyboardKeys.LWin
              || e.KeyCode == KeyboardKeys.RWin)
        {
            IsExtendedKey = e.KeyCode == KeyboardKeys.RWin;
            IsWin = false;
            return;
        }
        else if (e.KeyCode == KeyboardKeys.Alt
              || e.KeyCode == KeyboardKeys.LMenu
              || e.KeyCode == KeyboardKeys.RMenu)
        {
            IsExtendedKey = e.KeyCode == KeyboardKeys.RMenu;
            IsAlt = false;
            return;
        }
        else if (e.KeyCode == KeyboardKeys.Return)
        {
            IsExtendedKey = KeyboardKeys.Return.IsExtendedKey();
        }
    }
}
