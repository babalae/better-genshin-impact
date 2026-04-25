using System;
using System.Collections.Generic;
using BetterGenshinImpact.Helpers;
using Fischless.GameCapture;

namespace BetterGenshinImpact.GameTask;

public class CaptureService
{
    public IGameCapture? GameCapture { get; private set; }

    public bool IsCapturing => GameCapture?.IsCapturing == true;

    public void Start(IntPtr hWnd, CaptureModes mode)
    {
        Stop();

        GameCapture = GameCaptureFactory.Create(mode);
        GameCapture.Start(hWnd,
            new Dictionary<string, object>
            {
                { "autoFixWin11BitBlt", OsVersionHelper.IsWindows11_OrGreater && TaskContext.Instance().Config.AutoFixWin11BitBlt }
            }
        );
    }

    public void Stop()
    {
        GameCapture?.Stop();
        GameCapture = null;
    }

    public IGameCapture RequireGameCapture()
    {
        if (GameCapture == null)
        {
            throw new Exception("截图器未初始化!");
        }

        return GameCapture;
    }
}
