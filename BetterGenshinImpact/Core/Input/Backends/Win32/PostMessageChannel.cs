using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Helpers;
using System;
using System.Drawing;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Input.Backends.Win32;

/// <summary>
/// 后台通道：包装 PostMessageSimulator，向游戏窗口投递消息。
/// 游戏不在前台时，每次操作前先补发 WM_ACTIVATE。
/// 不支持相对移动、中键、侧键、滚轮（warn 后忽略）
/// </summary>
internal sealed class PostMessageChannel : InputChannelBase
{
    private readonly IntPtr _hWnd;
    private readonly PostMessageSimulator? _simulator;

    /// <summary>
    /// 后续点击使用的客户区坐标。未调用过 MoveMouseTo 时沿用原实现的 (16,16)
    /// </summary>
    private Point _pointer = new(16, 16);

    public PostMessageChannel(IntPtr hWnd)
    {
        _hWnd = hWnd;
        _simulator = hWnd == IntPtr.Zero ? null : new PostMessageSimulator(hWnd);
    }

    protected override void OnKeyDown(User32.VK key)
    {
        if (TryPrepare("按键"))
        {
            _simulator!.KeyDown(key);
        }
    }

    protected override void OnKeyUp(User32.VK key)
    {
        if (TryPrepare("按键"))
        {
            _simulator!.KeyUp(key);
        }
    }

    protected override void OnKeyPress(User32.VK key)
    {
        if (TryPrepare("按键"))
        {
            _simulator!.KeyPress(key);
        }
    }

    protected override void OnMouseButton(InputMouseButton button, bool down)
    {
        if (!IsSupported(button) || !TryPrepare("鼠标"))
        {
            return;
        }

        var (x, y) = (_pointer.X, _pointer.Y);
        _ = (button, down) switch
        {
            (InputMouseButton.Left, true) => _simulator!.LeftButtonDown(x, y),
            (InputMouseButton.Left, false) => _simulator!.LeftButtonUp(x, y),
            (InputMouseButton.Right, true) => _simulator!.RightButtonDown(x, y),
            _ => _simulator!.RightButtonUp(x, y),
        };
    }

    protected override void OnMouseClick(InputMouseButton button)
    {
        if (!IsSupported(button) || !TryPrepare("鼠标"))
        {
            return;
        }

        _ = button == InputMouseButton.Left
            ? _simulator!.LeftButtonClick(_pointer.X, _pointer.Y)
            : _simulator!.RightButtonClick(_pointer.X, _pointer.Y);
    }

    protected override void OnMoveBy(int dx, int dy) => WarnUnsupported("后台相对移动 MoveMouseBy");

    /// <summary>
    /// 换算成客户区坐标，记为后续点击的位置，不移动真实光标
    /// </summary>
    protected override void OnMoveTo(double absX, double absY)
    {
        if (_simulator is null)
        {
            WarnUnsupported("未绑定游戏窗口时的后台鼠标");
            return;
        }

        var screen = PrimaryScreen.WorkingArea;
        var point = new POINT(
            (int)Math.Round(absX * screen.Width / 65535d),
            (int)Math.Round(absY * screen.Height / 65535d));
        if (!User32.ScreenToClient(_hWnd, ref point))
        {
            WarnUnsupported("后台鼠标坐标换算");
            return;
        }

        _pointer = new Point(point.X, point.Y);
    }

    protected override void OnScroll(int clicks) => WarnUnsupported("后台滚轮 VerticalScroll");

    private bool IsSupported(InputMouseButton button)
    {
        if (button is InputMouseButton.Left or InputMouseButton.Right)
        {
            return true;
        }

        WarnUnsupported($"后台鼠标键 {button}");
        return false;
    }

    /// <summary>
    /// 检查是否绑定了窗口，游戏不在前台时补发 WM_ACTIVATE
    /// </summary>
    private bool TryPrepare(string operation)
    {
        if (_simulator is null)
        {
            WarnUnsupported($"未绑定游戏窗口时的后台{operation}");
            return false;
        }

        if (User32.GetForegroundWindow() != _hWnd)
        {
            _simulator.Activate();
        }

        return true;
    }
}
