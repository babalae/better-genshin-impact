using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Simulator;

/// <summary>
///     虚拟输入专用，不走硬体 backend。
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
    private readonly ILogger? _logger = App.GetService<ILogger<PostMessageSimulator>>();
    private static int _virtualOnlyWarningLogged;

    public PostMessageSimulator(IntPtr hWnd)
    {
        _hWnd = hWnd;
    }

    /// <summary>
    ///     指定位置并按下左键
    /// </summary>
    public PostMessageSimulator LeftButtonClick(int x, int y)
    {
        WarnVirtualInputOnly();
        IntPtr p = (y << 16) | x;
        User32.PostMessage(_hWnd, WM_LBUTTONDOWN, IntPtr.Zero, p);
        Thread.Sleep(100);
        User32.PostMessage(_hWnd, WM_LBUTTONUP, IntPtr.Zero, p);
        return this;
    }

    /// <summary>
    ///     指定位置并按下左键
    /// </summary>
    public PostMessageSimulator LeftButtonClickBackground(int x, int y)
    {
        WarnVirtualInputOnly();
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_ACTIVATE, 1, 0);
        var p = MakeLParam(x, y);
        User32.PostMessage(_hWnd, WM_LBUTTONDOWN, 1, p);
        Thread.Sleep(100);
        User32.PostMessage(_hWnd, WM_LBUTTONUP, 0, p);
        return this;
    }

    public static int MakeLParam(int x, int y) => (y << 16) | (x & 0xFFFF);

    public PostMessageSimulator LeftButtonClick()
    {
        WarnVirtualInputOnly();
        IntPtr p = (16 << 16) | 16;
        User32.PostMessage(_hWnd, WM_LBUTTONDOWN, IntPtr.Zero, p);
        Thread.Sleep(100);
        User32.PostMessage(_hWnd, WM_LBUTTONUP, IntPtr.Zero, p);
        return this;
    }

    public PostMessageSimulator LeftButtonClickBackground()
    {
        WarnVirtualInputOnly();
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_ACTIVATE, 1, 0);
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
        WarnVirtualInputOnly();
        User32.PostMessage(_hWnd, WM_LBUTTONDOWN, IntPtr.Zero);
        return this;
    }

    /// <summary>
    ///     默认位置左键释放
    /// </summary>
    public PostMessageSimulator LeftButtonUp()
    {
        WarnVirtualInputOnly();
        User32.PostMessage(_hWnd, WM_LBUTTONUP, IntPtr.Zero);
        return this;
    }

    /// <summary>
    ///     默认位置右键按下
    /// </summary>
    public PostMessageSimulator RightButtonDown()
    {
        WarnVirtualInputOnly();
        User32.PostMessage(_hWnd, WM_RBUTTONDOWN, IntPtr.Zero);
        return this;
    }

    /// <summary>
    ///     默认位置右键释放
    /// </summary>
    public PostMessageSimulator RightButtonUp()
    {
        WarnVirtualInputOnly();
        User32.PostMessage(_hWnd, WM_RBUTTONUP, IntPtr.Zero);
        return this;
    }

    public PostMessageSimulator RightButtonClick()
    {
        WarnVirtualInputOnly();
        IntPtr p = (16 << 16) | 16;
        User32.PostMessage(_hWnd, WM_RBUTTONDOWN, IntPtr.Zero, p);
        Thread.Sleep(100);
        User32.PostMessage(_hWnd, WM_RBUTTONUP, IntPtr.Zero, p);
        return this;
    }

    public PostMessageSimulator KeyPress(User32.VK vk)
    {
        WarnVirtualInputOnly();
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYDOWN, (nint)vk, 0x1e0001);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_CHAR, (nint)vk, 0x1e0001);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYUP, (nint)vk, unchecked((nint)0xc01e0001));
        return this;
    }

    public PostMessageSimulator KeyPress(User32.VK vk, int ms)
    {
        WarnVirtualInputOnly();
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYDOWN, (nint)vk, 0x1e0001);
        Thread.Sleep(ms);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_CHAR, (nint)vk, 0x1e0001);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYUP, (nint)vk, unchecked((nint)0xc01e0001));
        return this;
    }

    public PostMessageSimulator LongKeyPress(User32.VK vk)
    {
        WarnVirtualInputOnly();
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYDOWN, (nint)vk, 0x1e0001);
        Thread.Sleep(1000);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_CHAR, (nint)vk, 0x1e0001);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYUP, (nint)vk, unchecked((nint)0xc01e0001));
        return this;
    }

    public PostMessageSimulator KeyDown(User32.VK vk)
    {
        WarnVirtualInputOnly();
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYDOWN, (nint)vk, 0x1e0001);
        return this;
    }

    public PostMessageSimulator KeyUp(User32.VK vk)
    {
        WarnVirtualInputOnly();
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYUP, (nint)vk, unchecked((nint)0xc01e0001));
        return this;
    }

    public PostMessageSimulator KeyPressBackground(User32.VK vk)
    {
        WarnVirtualInputOnly();
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_ACTIVATE, 1, 0);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYDOWN, (nint)vk, 0x1e0001);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_CHAR, (nint)vk, 0x1e0001);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYUP, (nint)vk, unchecked((nint)0xc01e0001));
        return this;
    }

    public PostMessageSimulator KeyDownBackground(User32.VK vk)
    {
        WarnVirtualInputOnly();
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_ACTIVATE, 1, 0);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYDOWN, (nint)vk, 0x1e0001);
        return this;
    }

    public PostMessageSimulator KeyUpBackground(User32.VK vk)
    {
        WarnVirtualInputOnly();
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_ACTIVATE, 1, 0);
        User32.PostMessage(_hWnd, User32.WindowMessage.WM_KEYUP, (nint)vk, unchecked((nint)0xc01e0001));
        return this;
    }

    public PostMessageSimulator Sleep(int ms)
    {
        Thread.Sleep(ms);
        return this;
    }

    private void WarnVirtualInputOnly()
    {
        if (Interlocked.Exchange(ref _virtualOnlyWarningLogged, 1) == 0)
        {
            _logger?.LogWarning("PostMessage input path is virtual-input only and bypasses hardware backends.");
        }
    }
}
