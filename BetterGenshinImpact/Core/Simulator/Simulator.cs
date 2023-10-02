using System;

namespace BetterGenshinImpact.Core.Simulator;

public class Simulator
{
    public static PostMessageSimulator PostMessage(IntPtr hWnd)
    {
        return new PostMessageSimulator(hWnd);
    }
}