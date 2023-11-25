using System.Diagnostics;

namespace Fischless.KeyboardCapture;

[DebuggerDisplay("{result.ToString()}")]
public class KeyboardReader : IDisposable
{
    public static KeyboardReader Default { get; } = new();

    public event EventHandler<KeyboardResult> Received = null!;

    public bool IsCombinationOnly = false;
    public bool IsCaseSensitived = false;
    protected KeyboardHook KeyboardHook = new();
    protected bool IsShift = false;
    protected bool IsCtrl = false;
    protected bool IsAlt = false;
    protected bool IsWin = false;

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

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Shift
         || e.KeyCode == Keys.ShiftKey
         || e.KeyCode == Keys.LShiftKey
         || e.KeyCode == Keys.RShiftKey)
        {
            IsShift = true;
            if (IsCombinationOnly)
            {
                return;
            }
        }
        else if (e.KeyCode == Keys.Control
              || e.KeyCode == Keys.ControlKey
              || e.KeyCode == Keys.LControlKey
              || e.KeyCode == Keys.RControlKey)
        {
            IsCtrl = true;
            if (IsCombinationOnly)
            {
                return;
            }
        }
        else if (e.KeyCode == Keys.LWin
              || e.KeyCode == Keys.RWin)
        {
            IsWin = true;
            if (IsCombinationOnly)
            {
                return;
            }
        }
        else if (e.KeyCode == Keys.Alt
              || e.KeyCode == Keys.LMenu
              || e.KeyCode == Keys.RMenu)
        {
            IsAlt = true;
            if (IsCombinationOnly)
            {
                return;
            }
        }

        var now = DateTime.Now;

#if FALSE
        Debug.WriteLine(e.KeyCode);
#endif

        KeyboardItem item;

        if (IsCaseSensitived)
        {
            bool isUpper = Control.IsKeyLocked(Keys.CapsLock) ? !IsShift : IsShift;

            if (isUpper)
            {
                item = new(now, e.KeyCode, char.ToUpper((char)e.KeyCode).ToString());
            }
            else
            {
                item = new(now, e.KeyCode, char.ToLower((char)e.KeyCode).ToString());
            }
        }
        else
        {
            item = new(now, e.KeyCode);
        }

        KeyboardResult result = new(item)
        {
            IsShift = IsShift,
            IsCtrl = IsCtrl,
            IsAlt = IsAlt,
            IsWin = IsWin,
        };

        Received?.Invoke(this, result);
#if FALSE
        Debug.WriteLine(result.ToString());
#endif
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Shift
         || e.KeyCode == Keys.ShiftKey
         || e.KeyCode == Keys.LShiftKey
         || e.KeyCode == Keys.RShiftKey)
        {
            IsShift = false;
            return;
        }
        else if (e.KeyCode == Keys.Control
              || e.KeyCode == Keys.ControlKey
              || e.KeyCode == Keys.LControlKey
              || e.KeyCode == Keys.RControlKey)
        {
            IsCtrl = false;
            return;
        }
        else if (e.KeyCode == Keys.LWin
              || e.KeyCode == Keys.RWin)
        {
            IsWin = false;
            return;
        }
        else if (e.KeyCode == Keys.Alt
              || e.KeyCode == Keys.LMenu
              || e.KeyCode == Keys.RMenu)
        {
            IsAlt = false;
            return;
        }
    }
}
