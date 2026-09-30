using System;

namespace BetterGenshinImpact.Core.Input;

/// <summary>
/// 一组可以整体切换的输入输出：Win32 (SendInput + PostMessage)、WebSdk、Gamepad
/// </summary>
public interface IInputBackend : IDisposable
{
    InputBackendKind Kind { get; }

    /// <summary>
    /// 前台通道
    /// </summary>
    IInputChannel Foreground { get; }

    /// <summary>
    /// 后台通道。WebSdk / Gamepad 没有前后台之分，与 <see cref="Foreground"/> 是同一实例
    /// </summary>
    IInputChannel Background { get; }

    /// <summary>
    /// 释放所有通道中处于按下状态的按键
    /// </summary>
    void ReleaseAll();
}
