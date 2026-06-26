using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace BetterGenshinImpact.Core.Simulator;

public static class VirtualXbox360Controller
{
    public const int DefaultPickYHoldMilliseconds = 140;
    private const int MinimumYHoldMilliseconds = 30;

    private static readonly object SyncRoot = new();
    private static ViGEmClient? _client;
    private static IXbox360Controller? _controller;
    private static DateTime _lastFailureLogUtc = DateTime.MinValue;
    private static bool _processExitRegistered;

    public static bool EnsureConnected(ILogger logger)
    {
        lock (SyncRoot)
        {
            if (_controller != null)
            {
                return true;
            }

            try
            {
                _client = new ViGEmClient();
                _controller = _client.CreateXbox360Controller();
                _controller.AutoSubmitReport = true;
                _controller.Connect();
                RegisterProcessExitCleanup();
                logger.LogInformation("虚拟Xbox 360手柄已连接，用于发送自动拾取手柄Y键");
                return true;
            }
            catch (Exception e)
            {
                DisposeController();
                LogFailure(logger, e, "连接虚拟Xbox 360手柄失败，请确认 ViGEmBus 驱动可用");
                return false;
            }
        }
    }

    public static int NormalizeYHoldMilliseconds(int? holdMilliseconds)
    {
        return Math.Max(holdMilliseconds ?? DefaultPickYHoldMilliseconds, MinimumYHoldMilliseconds);
    }

    public static bool PressY(ILogger logger, int? holdMilliseconds = null)
    {
        lock (SyncRoot)
        {
            if (!EnsureConnected(logger) || _controller == null)
            {
                return false;
            }

            try
            {
                var normalizedHoldMilliseconds = NormalizeYHoldMilliseconds(holdMilliseconds);
                _controller.SetButtonState(Xbox360Button.Y, true);
                Thread.Sleep(normalizedHoldMilliseconds);
                _controller.SetButtonState(Xbox360Button.Y, false);
                logger.LogInformation("自动拾取：已发送虚拟Xbox 360手柄Y键，按住{Milliseconds}ms", normalizedHoldMilliseconds);
                return true;
            }
            catch (Exception e)
            {
                DisposeController();
                LogFailure(logger, e, "发送虚拟Xbox 360手柄Y键失败");
                return false;
            }
        }
    }

    private static void RegisterProcessExitCleanup()
    {
        if (_processExitRegistered)
        {
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            lock (SyncRoot)
            {
                DisposeController();
            }
        };
        _processExitRegistered = true;
    }

    private static void DisposeController()
    {
        try
        {
            _controller?.Disconnect();
        }
        catch
        {
            // Ignore cleanup failures during shutdown or reconnect.
        }
        finally
        {
            _controller = null;
            _client?.Dispose();
            _client = null;
        }
    }

    private static void LogFailure(ILogger logger, Exception exception, string message)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastFailureLogUtc).TotalSeconds < 30)
        {
            return;
        }

        _lastFailureLogUtc = now;
        logger.LogError(exception, message);
    }
}
