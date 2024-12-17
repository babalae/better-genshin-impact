using System.Collections;
using Vanara.PInvoke;

namespace Fischless.WindowsInput;
public class InputBuilder : IEnumerable<User32.INPUT>, IEnumerable
{
    public InputBuilder()
    {
        _inputList = [];
    }

    public User32.INPUT[] ToArray()
    {
        return [.. _inputList];
    }

    public IEnumerator<User32.INPUT> GetEnumerator()
    {
        return _inputList.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public User32.INPUT this[int position] => _inputList[position];

    public static bool IsExtendedKey(User32.VK keyCode)
    {
        return
            keyCode == User32.VK.VK_MENU
         || keyCode == User32.VK.VK_LMENU
         || keyCode == User32.VK.VK_RMENU
         || keyCode == User32.VK.VK_CONTROL
         || keyCode == User32.VK.VK_RCONTROL
         || keyCode == User32.VK.VK_INSERT
         || keyCode == User32.VK.VK_DELETE
         || keyCode == User32.VK.VK_HOME
         || keyCode == User32.VK.VK_END
         || keyCode == User32.VK.VK_PRIOR
         || keyCode == User32.VK.VK_NEXT
         || keyCode == User32.VK.VK_RIGHT
         || keyCode == User32.VK.VK_UP
         || keyCode == User32.VK.VK_LEFT
         || keyCode == User32.VK.VK_DOWN
         || keyCode == User32.VK.VK_NUMLOCK
         || keyCode == User32.VK.VK_CANCEL
         || keyCode == User32.VK.VK_SNAPSHOT
         || keyCode == User32.VK.VK_DIVIDE;
    }

    public InputBuilder AddKeyDown(User32.VK keyCode, bool? isExtendedKey = null)
    {
        bool isUseExtendedKey = isExtendedKey == null ? IsExtendedKey(keyCode) : isExtendedKey.Value;

        if ((VK2)keyCode == VK2.VK_NUMPAD_ENTER)
        {
            keyCode = User32.VK.VK_RETURN;
            isUseExtendedKey = true;
        }

        User32.INPUT input = new()
        {
            type = User32.INPUTTYPE.INPUT_KEYBOARD,
            ki = new User32.KEYBDINPUT()
            {
                wVk = (ushort)keyCode,
                wScan = (ushort)(User32.MapVirtualKey((uint)keyCode, 0) & 0xFFU),
                dwFlags = (isUseExtendedKey ? User32.KEYEVENTF.KEYEVENTF_EXTENDEDKEY : 0),
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        };
        User32.INPUT item = input;
        _inputList.Add(item);
        return this;
    }

    public InputBuilder AddKeyUp(User32.VK keyCode, bool? isExtendedKey = null)
    {
        bool isUseExtendedKey = isExtendedKey == null ? IsExtendedKey(keyCode) : isExtendedKey.Value;

        if ((VK2)keyCode == VK2.VK_NUMPAD_ENTER)
        {
            keyCode = User32.VK.VK_RETURN;
            isUseExtendedKey = true;
        }

        User32.INPUT input = new()
        {
            type = User32.INPUTTYPE.INPUT_KEYBOARD,
            ki = new User32.KEYBDINPUT()
            {
                wVk = (ushort)keyCode,
                wScan = (ushort)(User32.MapVirtualKey((uint)keyCode, 0) & 0xFFU),
                dwFlags = (isUseExtendedKey ? User32.KEYEVENTF.KEYEVENTF_EXTENDEDKEY : 0) | User32.KEYEVENTF.KEYEVENTF_KEYUP,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        };
        User32.INPUT item = input;
        _inputList.Add(item);
        return this;
    }

    public InputBuilder AddKeyPress(User32.VK keyCode, bool? isExtendedKey = null)
    {
        AddKeyDown(keyCode, isExtendedKey);
        AddKeyUp(keyCode, isExtendedKey);
        return this;
    }

    public InputBuilder AddCharacter(char character)
    {
        User32.INPUT input = new()
        {
            type = User32.INPUTTYPE.INPUT_KEYBOARD,
            ki = new User32.KEYBDINPUT()
            {
                wVk = 0,
                wScan = character,
                dwFlags = User32.KEYEVENTF.KEYEVENTF_UNICODE,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        };
        User32.INPUT item = input;
        User32.INPUT input2 = new()
        {
            type = User32.INPUTTYPE.INPUT_KEYBOARD,
            ki = new User32.KEYBDINPUT()
            {
                wVk = 0,
                wScan = character,
                dwFlags = User32.KEYEVENTF.KEYEVENTF_KEYUP | User32.KEYEVENTF.KEYEVENTF_UNICODE,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        };
        User32.INPUT item2 = input2;
        if ((character & '\u1234') == '\u1234')
        {
            item.ki = new User32.KEYBDINPUT()
            {
                wVk = item.ki.wVk,
                wScan = item.ki.wScan,
                dwFlags = item.ki.dwFlags | User32.KEYEVENTF.KEYEVENTF_EXTENDEDKEY,
                time = item.ki.time,
                dwExtraInfo = item.ki.dwExtraInfo,
            };
            item2.ki = new User32.KEYBDINPUT()
            {
                wVk = item2.ki.wVk,
                wScan = item2.ki.wScan,
                dwFlags = item2.ki.dwFlags | User32.KEYEVENTF.KEYEVENTF_EXTENDEDKEY,
                time = item2.ki.time,
                dwExtraInfo = item2.ki.dwExtraInfo,
            };
        }
        _inputList.Add(item);
        _inputList.Add(item2);
        return this;
    }

    public InputBuilder AddCharacters(IEnumerable<char> characters)
    {
        foreach (char character in characters)
        {
            AddCharacter(character);
        }
        return this;
    }

    public InputBuilder AddCharacters(string characters)
    {
        return AddCharacters(characters.ToCharArray());
    }

    public InputBuilder AddRelativeMouseMovement(int x, int y)
    {
        User32.INPUT item = new()
        {
            type = User32.INPUTTYPE.INPUT_MOUSE,
            mi = new User32.MOUSEINPUT()
            {
                dx = x,
                dy = y,
                dwFlags = User32.MOUSEEVENTF.MOUSEEVENTF_MOVE,
            },
        };
        _inputList.Add(item);
        return this;
    }

    public InputBuilder AddAbsoluteMouseMovement(int absoluteX, int absoluteY)
    {
        User32.INPUT item = new()
        {
            type = User32.INPUTTYPE.INPUT_MOUSE,
            mi = new User32.MOUSEINPUT()
            {
                dx = absoluteX,
                dy = absoluteY,
                dwFlags = User32.MOUSEEVENTF.MOUSEEVENTF_MOVE | User32.MOUSEEVENTF.MOUSEEVENTF_ABSOLUTE,
            },
        };
        _inputList.Add(item);
        return this;
    }

    public InputBuilder AddAbsoluteMouseMovementOnVirtualDesktop(int absoluteX, int absoluteY)
    {
        User32.INPUT item = new()
        {
            type = User32.INPUTTYPE.INPUT_MOUSE,
            mi = new User32.MOUSEINPUT()
            {
                dx = absoluteX,
                dy = absoluteY,
                dwFlags = User32.MOUSEEVENTF.MOUSEEVENTF_MOVE | User32.MOUSEEVENTF.MOUSEEVENTF_ABSOLUTE | User32.MOUSEEVENTF.MOUSEEVENTF_VIRTUALDESK,
            },
        };
        _inputList.Add(item);
        return this;
    }

    public InputBuilder AddMouseButtonDown(MouseButton button)
    {
        User32.INPUT item = new()
        {
            type = User32.INPUTTYPE.INPUT_MOUSE,
            mi = new User32.MOUSEINPUT()
            {
                dwFlags = ToMouseButtonDownFlag(button),
            },
        };
        _inputList.Add(item);
        return this;
    }

    public InputBuilder AddMouseXButtonDown(int xButtonId)
    {
        User32.INPUT item = new()
        {
            type = User32.INPUTTYPE.INPUT_MOUSE,
            mi = new User32.MOUSEINPUT()
            {
                dwFlags = User32.MOUSEEVENTF.MOUSEEVENTF_XDOWN,
                mouseData = xButtonId,
            },
        };
        _inputList.Add(item);
        return this;
    }

    public InputBuilder AddMouseButtonUp(MouseButton button)
    {
        User32.INPUT item = new()
        {
            type = User32.INPUTTYPE.INPUT_MOUSE,
            mi = new User32.MOUSEINPUT()
            {
                dwFlags = ToMouseButtonUpFlag(button),
            },
        };
        _inputList.Add(item);
        return this;
    }

    public InputBuilder AddMouseXButtonUp(int xButtonId)
    {
        User32.INPUT item = new()
        {
            type = User32.INPUTTYPE.INPUT_MOUSE,
            mi = new User32.MOUSEINPUT()
            {
                dwFlags = User32.MOUSEEVENTF.MOUSEEVENTF_XUP,
                mouseData = xButtonId,
            },
        };
        _inputList.Add(item);
        return this;
    }

    public InputBuilder AddMouseButtonClick(MouseButton button)
    {
        return AddMouseButtonDown(button).AddMouseButtonUp(button);
    }

    public InputBuilder AddMouseXButtonClick(int xButtonId)
    {
        return AddMouseXButtonDown(xButtonId).AddMouseXButtonUp(xButtonId);
    }

    public InputBuilder AddMouseButtonDoubleClick(MouseButton button)
    {
        return AddMouseButtonClick(button).AddMouseButtonClick(button);
    }

    public InputBuilder AddMouseXButtonDoubleClick(int xButtonId)
    {
        return AddMouseXButtonClick(xButtonId).AddMouseXButtonClick(xButtonId);
    }

    public InputBuilder AddMouseVerticalWheelScroll(int scrollAmount)
    {
        User32.INPUT item = new()
        {
            type = User32.INPUTTYPE.INPUT_MOUSE,
            mi = new User32.MOUSEINPUT()
            {
                dwFlags = User32.MOUSEEVENTF.MOUSEEVENTF_WHEEL,
                mouseData = scrollAmount,
            },
        };
        _inputList.Add(item);
        return this;
    }

    public InputBuilder AddMouseHorizontalWheelScroll(int scrollAmount)
    {
        User32.INPUT item = new()
        {
            type = User32.INPUTTYPE.INPUT_MOUSE,
            mi = new User32.MOUSEINPUT()
            {
                dwFlags = User32.MOUSEEVENTF.MOUSEEVENTF_HWHEEL,
                mouseData = scrollAmount,
            },
        };
        _inputList.Add(item);
        return this;
    }

    private static User32.MOUSEEVENTF ToMouseButtonDownFlag(MouseButton button)
    {
        return button switch
        {
            MouseButton.LeftButton => User32.MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN,
            MouseButton.MiddleButton => User32.MOUSEEVENTF.MOUSEEVENTF_MIDDLEDOWN,
            MouseButton.RightButton => User32.MOUSEEVENTF.MOUSEEVENTF_RIGHTDOWN,
            _ => User32.MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN,
        };
    }

    private static User32.MOUSEEVENTF ToMouseButtonUpFlag(MouseButton button)
    {
        return button switch
        {
            MouseButton.LeftButton => User32.MOUSEEVENTF.MOUSEEVENTF_LEFTUP,
            MouseButton.MiddleButton => User32.MOUSEEVENTF.MOUSEEVENTF_MIDDLEUP,
            MouseButton.RightButton => User32.MOUSEEVENTF.MOUSEEVENTF_RIGHTUP,
            _ => User32.MOUSEEVENTF.MOUSEEVENTF_LEFTUP,
        };
    }

    private readonly List<User32.INPUT> _inputList;
}

/// <summary>
/// https://learn.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes
/// </summary>
public enum VK2
{
    /// <summary>
    ///  ENTER key
    /// </summary>
    VK_ENTER = User32.VK.VK_RETURN,

    /// <summary>
    ///  The Unassigned code: The Num ENTER key.
    /// </summary>
    VK_NUMPAD_ENTER = 0x0E,
}
