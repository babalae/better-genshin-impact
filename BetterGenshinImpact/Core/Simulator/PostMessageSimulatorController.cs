using System;
using System.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoSkip;
using GamepadController.Simulator;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Simulator;

/// <summary>
///     虚拟键代码 add
///     https://liujiahua.com/blog/2021/06/20/csharp-XInput/
///     https://learn.microsoft.com/zh-cn/windows/win32/xinput/directinput-and-xusb-devices
///     User32.VK.VK_SPACE 键盘空格键
/// </summary>
public class PostMessageSimulatorController
{
    private readonly IntPtr _hWnd;
    private readonly AllConfig _config;
    private ControllerSimulator? _controllerSimulator;
    private static readonly ILogger Logger = App.GetLogger<AutoSkipConfig>();

    public PostMessageSimulatorController(IntPtr hWnd, AllConfig config)
    {
        _hWnd = hWnd;
        _config = config;
        if (_config.AutoSkipControllerEnabled)
        {
            _controllerSimulator = new ControllerSimulator();
        }
    }

    public PostMessageSimulatorController ButtonXPress()
    {
        if (!_config.AutoSkipControllerEnabled)
        {
            return this;
        }

        _controllerSimulator?.OnButtonPressed(ControllerSimulator.ButtonX, 100);
        return this;
    }

    public PostMessageSimulatorController ButtonYPress()
    {
        if (!_config.AutoSkipControllerEnabled)
        {
            return this;
        }

        _controllerSimulator?.OnButtonPressed(ControllerSimulator.ButtonY, 100);
        return this;
    }

    public PostMessageSimulatorController DisconnectController()
    {
        _controllerSimulator?.BreakOffGamepad();
        return this;
    }

    public PostMessageSimulatorController ButtonAPress()
    {
        if (!_config.AutoSkipControllerEnabled)
        {
            return this;
        }

        _controllerSimulator?.OnButtonPressed(ControllerSimulator.ButtonA, 100);
        return this;
    }

    public PostMessageSimulatorController ButtonBPress()
    {
        if (!_config.AutoSkipControllerEnabled)
        {
            return this;
        }

        _controllerSimulator?.OnButtonPressed(ControllerSimulator.ButtonB, 100);
        return this;
    }
}