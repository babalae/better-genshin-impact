using BetterGenshinImpact.Core.Simulator;
using Fischless.WindowsInput;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Input.Backends.Win32;

/// <summary>
/// 前台通道：包装 Simulation.SendInput（Fischless.WindowsInput）
/// </summary>
internal sealed class SendInputChannel : InputChannelBase
{
    private static InputSimulator Input => Simulation.SendInput;

    // 扩展标志按真实键盘判断（见 ExtendedKeys）：左 Alt 等不带，避免被识别成右 Alt；方向键等带，避免被识别成小键盘按键。
    // 不使用 Fischless 的自动判断，它把左 Alt 与泛指的 Alt / Ctrl 也算作扩展键
    protected override void OnKeyDown(User32.VK key) => Input.Keyboard.KeyDown(ExtendedKeys.IsExtended(key), key);

    protected override void OnKeyUp(User32.VK key) => Input.Keyboard.KeyUp(ExtendedKeys.IsExtended(key), key);

    protected override void OnKeyPress(User32.VK key) => Input.Keyboard.KeyPress(ExtendedKeys.IsExtended(key), key);

    protected override void OnMouseButton(InputMouseButton button, bool down)
    {
        var mouse = Input.Mouse;
        switch (button)
        {
            case InputMouseButton.Left:
                _ = down ? mouse.LeftButtonDown() : mouse.LeftButtonUp();
                break;
            case InputMouseButton.Right:
                _ = down ? mouse.RightButtonDown() : mouse.RightButtonUp();
                break;
            case InputMouseButton.Middle:
                _ = down ? mouse.MiddleButtonDown() : mouse.MiddleButtonUp();
                break;
            case InputMouseButton.X1:
                _ = down ? mouse.XButtonDown(0x0001) : mouse.XButtonUp(0x0001);
                break;
            case InputMouseButton.X2:
                _ = down ? mouse.XButtonDown(0x0002) : mouse.XButtonUp(0x0002);
                break;
        }
    }

    protected override void OnMouseClick(InputMouseButton button)
    {
        var mouse = Input.Mouse;
        switch (button)
        {
            case InputMouseButton.Left:
                mouse.LeftButtonClick();
                break;
            case InputMouseButton.Right:
                mouse.RightButtonClick();
                break;
            case InputMouseButton.Middle:
                mouse.MiddleButtonClick();
                break;
            case InputMouseButton.X1:
                mouse.XButtonClick(0x0001);
                break;
            case InputMouseButton.X2:
                mouse.XButtonClick(0x0002);
                break;
        }
    }

    protected override void OnMoveBy(int dx, int dy) => Input.Mouse.MoveMouseBy(dx, dy);

    protected override void OnMoveTo(double absX, double absY) => Input.Mouse.MoveMouseTo(absX, absY);

    protected override void OnScroll(int clicks) => Input.Mouse.VerticalScroll(clicks);

    /// <summary>
    /// 与原 Simulation.IsKeyDown 一致，查询系统的实际按键状态
    /// </summary>
    public override bool IsKeyDown(User32.VK key) => Simulation.IsKeyDown(key);

    /// <summary>
    /// 按记录抬起，再用 Simulation.ReleaseAllKey 兜底（与原 Simulation.ReleaseAllKey 行为一致）
    /// </summary>
    public override void ReleaseAll()
    {
        base.ReleaseAll();
        Simulation.ReleaseAllKey();
    }
}
