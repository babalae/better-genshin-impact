using System;
using System.Threading;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Simulator;

/// <summary>
///     虚拟键代码
///     https://learn.microsoft.com/zh-cn/windows/win32/inputdev/virtual-key-codes
///     User32.VK.VK_SPACE 键盘空格键
/// </summary>
public class PostMessageSimulator
{
    public static readonly uint WM_LBUTTONDOWN = 0x201; //按下鼠标左键

    public static readonly uint WM_LBUTTONUP = 0x202; //释放鼠标左键

    public static readonly uint WM_RBUTTONDOWN = 0x204;
    public static readonly uint WM_RBUTTONUP = 0x205;

    private readonly IntPtr _hWnd;

    public PostMessageSimulator(IntPtr hWnd)
    {
        _hWnd = hWnd;
    }

    /// <summary>
    ///     指定位置并按下左键
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    public PostMessageSimulator LeftButtonClick(int x, int y)
    {
        IntPtr p = (y << 16) | x;
        User32.PostMessage(_hWnd, WM_LBUTTONDOWN, IntPtr.Zero, p);
        Thread.Sleep(100);
        User32.PostMessage(_hWnd, WM_LBUTTONUP, IntPtr.Zero, p);
        return this;
    }

    public PostMessageSimulator LeftButtonClick()
    {
        IntPtr p = (16 << 16) | 16;
        User32.PostMessage(_hWnd, WM_LBUTTONDOWN, IntPtr.Zero, p);
        Thread.Sleep(100);
        User32.PostMessage(_hWnd, WM_LBUTTONUP, IntPtr.Zero, p);
        return this;
    }

    /// <summary>
    ///     默认位置左键按下
    /// </summary>
    public PostMessageSimulator LeftButtonDown()
    {
        User32.PostMessage(_hWnd, WM_LBUTTONDOWN, IntPtr.Zero);
        return this;
    }

    /// <summary>
    ///     默认位置左键释放
    /// </summary>
    public PostMessageSimulator LeftButtonUp()
    {
        User32.PostMessage(_hWnd, WM_LBUTTONUP, IntPtr.Zero);
        return this;
    }

    /// <summary>
    ///     默认位置右键按下
    /// </summary>
    public PostMessageSimulator RightButtonDown()
    {
        User32.PostMessage(_hWnd, WM_RBUTTONDOWN, IntPtr.Zero);
        return this;
    }

    /// <summary>
    ///     默认位置右键释放
    /// </summary>
    public PostMessageSimulator RightButtonUp()
    {
        User32.PostMessage(_hWnd, WM_RBUTTONUP, IntPtr.Zero);
        return this;
    }

    public PostMessageSimulator RightButtonClick()
    {
        IntPtr p = (16 << 16) | 16;
        User32.PostMessage(_hWnd, WM_RBUTTONDOWN, IntPtr.Zero, p);
        Thread.Sleep(100);
        User32.PostMessage(_hWnd, WM_RBUTTONUP, IntPtr.Zero, p);
        return this;
    }

    public PostMessageSimulator KeyPress(User32.VK vk)
    {
        //User32.PostMessage(_hWnd, User32.WindowMessage.WM_ACTIVATE, 1, 0);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYDOWN, (nint)vk, 0x1e0001);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_CHAR, (nint)vk, 0x1e0001);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYUP, (nint)vk, (nint)0xc01e0001);
        return this;
    }

    public PostMessageSimulator KeyPress(User32.VK vk, int ms)
    {
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYDOWN, (nint)vk, 0x1e0001);
        Thread.Sleep(ms);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_CHAR, (nint)vk, 0x1e0001);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYUP, (nint)vk, (nint)0xc01e0001);
        return this;
    }

    public PostMessageSimulator LongKeyPress(User32.VK vk)
    {
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYDOWN, (nint)vk, 0x1e0001);
        Thread.Sleep(1000);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_CHAR, (nint)vk, 0x1e0001);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYUP, (nint)vk, (nint)0xc01e0001);
        return this;
    }

    public PostMessageSimulator KeyDown(User32.VK vk)
    {
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYDOWN, (nint)vk, 0x1e0001);
        return this;
    }

    public PostMessageSimulator KeyUp(User32.VK vk)
    {
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYUP, (nint)vk, (nint)0xc01e0001);
        return this;
    }

    public PostMessageSimulator KeyPressBackground(User32.VK vk)
    {
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_ACTIVATE, 1, 0);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYDOWN, (nint)vk, 0x1e0001);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_CHAR, (nint)vk, 0x1e0001);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYUP, (nint)vk, (nint)0xc01e0001);
        return this;
    }

    public PostMessageSimulator KeyDownBackground(User32.VK vk)
    {
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_ACTIVATE, 1, 0);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYDOWN, (nint)vk, 0x1e0001);
        return this;
    }

    public PostMessageSimulator KeyUpBackground(User32.VK vk)
    {
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_ACTIVATE, 1, 0);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYUP, (nint)vk, (nint)0xc01e0001);
        return this;
    }

    public PostMessageSimulator Sleep(int ms)
    {
        Thread.Sleep(ms);
        return this;
    }
}
