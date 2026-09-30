using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Input.Backends.Win32;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Windows;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Runtime.Win32;

/// <summary>
/// 本机 Win32 游戏窗口的运行环境：本地原神、Windows 云原神
/// </summary>
public sealed class Win32RuntimeProvider(IConfigService configService, ILogger<Win32RuntimeProvider> logger)
    : IGameRuntimeProvider
{
    public GameRuntimeKind Kind => GameRuntimeKind.Win32Window;

    /// <summary>
    /// 找到游戏窗口就附着；找不到时，开启了关联启动则启动游戏后附着，否则提示并返回 null
    /// </summary>
    public async Task<GameRuntime?> AcquireAsync(CancellationToken ct)
    {
        var config = configService.Get();
        DisableGenshinHdrIfNeeded(config);

        var hWnd = SystemControl.FindGenshinImpactHandle();
        if (hWnd == IntPtr.Zero)
        {
            var startConfig = config.GenshinStartConfig;
            if (startConfig.LinkedStartEnabled)
            {
                if (string.IsNullOrEmpty(startConfig.InstallPath))
                {
                    await ThemedMessageBox.ErrorAsync("没有找到原神的安装路径");
                    return null;
                }

                hWnd = await SystemControl.StartFromLocalAsync(startConfig.InstallPath);
                if (hWnd == IntPtr.Zero)
                {
                    // 启动失败的提示已由 StartFromLocalAsync 给出
                    return null;
                }
            }

            if (hWnd == IntPtr.Zero)
            {
                await ThemedMessageBox.ErrorAsync("未找到原神窗口，请先启动原神！");
                return null;
            }
        }

        ct.ThrowIfCancellationRequested();
        return AttachTo(hWnd);
    }

    /// <summary>
    /// 附着到指定的游戏窗口，手动选窗也走这里。在 UI 线程上调用
    /// </summary>
    public GameRuntime AttachTo(nint hWnd)
    {
        // 激活窗口，保证后面能够正常获取窗口信息
        SystemControl.ActivateWindow(hWnd);

        var window = new Win32GameWindow(hWnd);
        IGameCapture? capture = null;
        try
        {
            if (window.IsMinimized)
            {
                throw new ArgumentException("游戏窗口不能最小化");
            }

            var config = configService.Get();
            capture = GameCaptureFactory.Create(GetCaptureMode(config));
            capture.Start(hWnd, GameCaptureSettings.From(config));
            return new GameRuntime(Kind, window, capture, new Win32InputBackend(hWnd));
        }
        catch
        {
            capture?.Dispose();
            window.Dispose();
            throw;
        }
    }

    public void CloseGame() => SystemControl.CloseGameProcesses();

    private static CaptureModes GetCaptureMode(AllConfig config)
    {
        try
        {
            return config.CaptureMode.ToCaptureMode();
        }
        catch (Exception)
        {
            config.CaptureMode = CaptureModes.BitBlt.ToString();
            return CaptureModes.BitBlt;
        }
    }

    private void DisableGenshinHdrIfNeeded(AllConfig config)
    {
        if (!config.GenshinStartConfig.AutoDisableGenshinHdrEnabled)
        {
            return;
        }

        if (!GenshinHdrRegistryHelper.TryDisableHdr(out _))
        {
            return;
        }

        // 这行日志可能看不到
        logger.LogWarning("检测到原神 HDR 已开启并已自动关闭。如游戏已在运行，请重启游戏后生效。");
    }
}
