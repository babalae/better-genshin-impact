using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using System;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Input.Backends.WebSdk;

/// <summary>
/// 云原神网页版后端：把通道操作翻译成页面 SDK（ys-input-inject.js）的函数调用。
/// 网页版没有前后台之分，后端自身就是唯一的通道，<see cref="Foreground"/> 与 <see cref="Background"/> 都返回自己。
/// <para>
/// 指针位置由 SDK 保存（setAbsPosition），按键和相对移动默认使用它；按住时长使用 SDK 的默认值
/// （keyHoldMs 60 ms、clickHoldMs 35 ms），需要调整时通过 <c>Sdk.Invoke("setDefaults", …)</c>。
/// </para>
/// 由 WebPageRuntimeProvider 创建，GameRuntimeService 负责 InputHub.Attach。
/// </summary>
public sealed class WebSdkInputBackend : InputChannelBase, IInputBackend
{
    // TODO(云原神网页版)：鼠标相对移动的偏差值尚未实测，暂按 1:1 透传。
    // dx/dy 原样作为 ClientCore.mouseMove 的 rel 参数发送，与桌面端 SendInput 的相对位移是否等价尚未验证。
    // 调用方传入前通常已乘过 TaskContext.DpiScale，标定时需要一并考虑。
    // 实测后只调整这两个系数，不要在业务代码里补偿。
    private const double RelativeMoveScaleX = 1.0;
    private const double RelativeMoveScaleY = 1.0;

    // TODO(云原神网页版)：滚轮的方向和步长尚未实测，暂按 Windows 的 120/格 透传。
    private const int WheelDeltaPerClick = 120;

    private readonly Func<RECT> _canvasRect;

    /// <param name="sdk">SDK 调用桥，由宿主持有并释放</param>
    /// <param name="canvasRect">游戏画面在屏幕上的物理像素矩形，应与截图区域一致</param>
    public WebSdkInputBackend(IWebInputBridge sdk, Func<RECT> canvasRect)
    {
        Sdk = sdk ?? throw new ArgumentNullException(nameof(sdk));
        _canvasRect = canvasRect ?? throw new ArgumentNullException(nameof(canvasRect));
    }

    /// <summary>
    /// 扩展口：直接调用 SDK 的其他函数，用于键鼠接口表达不了的需求，例如 sendIme、sendClipboard、setDefaults、look
    /// </summary>
    public IWebInputBridge Sdk { get; }

    public InputBackendKind Kind => InputBackendKind.WebSdk;

    public IInputChannel Foreground => this;

    public IInputChannel Background => this;

    #region 原子操作

    protected override void OnKeyDown(User32.VK key) => InvokeKey("keyDown", key);

    protected override void OnKeyUp(User32.VK key) => InvokeKey("keyUp", key);

    protected override void OnKeyPress(User32.VK key) => InvokeKey("tapKey", key);

    protected override void OnMouseButton(InputMouseButton button, bool down)
    {
        if (TryGetButtonName(button, out var name))
        {
            Sdk.Invoke(down ? "mouseDown" : "mouseUp", name);
        }
    }

    protected override void OnMouseClick(InputMouseButton button)
    {
        if (TryGetButtonName(button, out var name))
        {
            Sdk.Invoke("click", name);
        }
    }

    protected override void OnMoveBy(int dx, int dy) =>
        Sdk.Invoke("mouseMove", dx * RelativeMoveScaleX, dy * RelativeMoveScaleY);

    /// <summary>
    /// 桌面 0~65535 坐标 -> 屏幕物理像素 -> 相对游戏画面归一化到 0~1
    /// </summary>
    protected override void OnMoveTo(double absX, double absY)
    {
        var rect = _canvasRect();
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            WarnUnsupported("游戏画面区域为空时的鼠标定位");
            return;
        }

        var screen = PrimaryScreen.WorkingArea;
        var x = Math.Clamp((absX * screen.Width / 65535d - rect.Left) / rect.Width, 0d, 1d);
        var y = Math.Clamp((absY * screen.Height / 65535d - rect.Top) / rect.Height, 0d, 1d);

        // 位置由 SDK 保存，后续按键和相对移动都使用它
        Sdk.Invoke("setAbsPosition", x, y);
        Sdk.Invoke("mouseMove", 0, 0);
    }

    protected override void OnScroll(int clicks) => Sdk.Invoke("scroll", clicks * WheelDeltaPerClick);

    #endregion

    /// <summary>
    /// SDK 的 releaseAll 会释放页面侧记录的全部按键，本地记录同步清空。
    /// 同时满足 <see cref="IInputChannel.ReleaseAll"/> 与 <see cref="IInputBackend.ReleaseAll"/>
    /// </summary>
    public override void ReleaseAll()
    {
        TakePressedKeys();
        try
        {
            Sdk.Invoke("releaseAll");
        }
        catch (InvalidOperationException e)
        {
            // 页面或桥接已关闭时无法再发送释放指令。ReleaseAll 常在 finally 中调用，不能让它抛出并掩盖原异常
            Logger.LogDebug(e, "云原神页面已关闭，跳过释放按键");
        }
    }

    /// <summary>
    /// 桥接由宿主持有并释放，这里不释放
    /// </summary>
    public void Dispose()
    {
    }

    private void InvokeKey(string method, User32.VK key)
    {
        if (WebKeyCodes.TryGetCode(key, out var code))
        {
            Sdk.Invoke(method, code);
        }
        else
        {
            WarnUnsupported($"按键 {key}");
        }
    }

    private bool TryGetButtonName(InputMouseButton button, out string name)
    {
        name = button switch
        {
            InputMouseButton.Left => "left",
            InputMouseButton.Right => "right",
            InputMouseButton.Middle => "middle",
            _ => string.Empty,
        };
        if (name.Length > 0)
        {
            return true;
        }

        WarnUnsupported($"鼠标键 {button}");
        return false;
    }
}
