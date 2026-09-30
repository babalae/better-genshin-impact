using System;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Input.Backends.WebSdk;

/// <summary>
/// 云原神网页版后端。网页版没有前后台之分，前后台返回同一个通道。
/// <para>
/// 宿主接入步骤：注入 SDK 与分发脚本 -> 创建 <see cref="WebView2InputBridge"/> ->
/// 在 TaskContext.Init 之后 <c>InputHub.Attach(new WebSdkInputBackend(bridge, GetCanvasRect))</c>
/// -> 订阅 InvokeFailed -> 页面关闭前 InputHub.ReleaseAll()。
/// </para>
/// </summary>
public sealed class WebSdkInputBackend : IInputBackend
{
    private readonly WebSdkChannel _channel;

    /// <param name="sdk">SDK 调用桥，由宿主持有并释放</param>
    /// <param name="canvasRect">游戏画面在屏幕上的物理像素矩形，应与截图区域一致</param>
    public WebSdkInputBackend(IWebInputBridge sdk, Func<RECT> canvasRect)
    {
        Sdk = sdk ?? throw new ArgumentNullException(nameof(sdk));
        _channel = new WebSdkChannel(sdk, canvasRect ?? throw new ArgumentNullException(nameof(canvasRect)));
    }

    /// <summary>
    /// 直接调用 SDK 的其他函数，用于键鼠接口表达不了的需求，例如 sendIme、setDefaults、look
    /// </summary>
    public IWebInputBridge Sdk { get; }

    public InputBackendKind Kind => InputBackendKind.WebSdk;

    public IInputChannel Foreground => _channel;

    public IInputChannel Background => _channel;

    public void ReleaseAll() => _channel.ReleaseAll();

    /// <summary>
    /// 桥接由宿主持有并释放，这里不释放
    /// </summary>
    public void Dispose()
    {
    }
}
