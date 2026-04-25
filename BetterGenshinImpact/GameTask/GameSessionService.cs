using System;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask;

public class GameSessionService
{
    public bool IsAttached => TaskContext.Instance().IsInitialized;

    public void Attach(IntPtr hWnd)
    {
        TaskContext.Instance().Init(hWnd);
    }

    public void Detach()
    {
        TaskContext.Instance().IsInitialized = false;
    }

    public void UpdateCaptureRect(RECT rect)
    {
        if (!IsAttached)
        {
            return;
        }

        TaskContext.Instance().SystemInfo.CaptureAreaRect = rect;
    }
}
