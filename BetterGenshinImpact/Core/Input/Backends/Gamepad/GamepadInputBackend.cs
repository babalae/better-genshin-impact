using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Input.Backends.Gamepad;

/// <summary>
/// 手柄后端（预留，未实现）。前后台返回同一个通道，所有操作都 warn 后忽略。
/// </summary>
public sealed class GamepadInputBackend : IInputBackend
{
    private readonly GamepadChannel _channel = new();

    public InputBackendKind Kind => InputBackendKind.Gamepad;

    public IInputChannel Foreground => _channel;

    public IInputChannel Background => _channel;

    public void ReleaseAll() => _channel.ReleaseAll();

    public void Dispose()
    {
    }
}

/// <summary>
/// 手柄通道（预留，未实现）。
/// <para>实现时的约定：</para>
/// <list type="bullet">
/// <item>覆写 SimulateAction，把 GIActions 直接映射成手柄输入；</item>
/// <item>键盘按键按键位配置反查成 GIActions 再映射；</item>
/// <item>MoveMouseBy 映射到右摇杆，是阻塞调用；</item>
/// <item>原始鼠标按键和 MoveMouseTo 都 warn 后忽略。坐标点击由“移动 + 左键”两步组成，只忽略移动的话，左键会被映射成普攻。</item>
/// </list>
/// </summary>
internal sealed class GamepadChannel : InputChannelBase
{
    private const string NotImplemented = "手柄后端未实现";

    protected override void OnKeyDown(User32.VK key) => WarnUnsupported(NotImplemented);

    protected override void OnKeyUp(User32.VK key) => WarnUnsupported(NotImplemented);

    protected override void OnMouseButton(InputMouseButton button, bool down) => WarnUnsupported(NotImplemented);

    protected override void OnMoveBy(int dx, int dy) => WarnUnsupported(NotImplemented);

    protected override void OnMoveTo(double absX, double absY) => WarnUnsupported(NotImplemented);

    protected override void OnScroll(int clicks) => WarnUnsupported(NotImplemented);

    /// <summary>
    /// 未实现时没有真正按下的键
    /// </summary>
    public override bool IsKeyDown(User32.VK key) => false;

    public override void ReleaseAll() => TakePressedKeys();
}
