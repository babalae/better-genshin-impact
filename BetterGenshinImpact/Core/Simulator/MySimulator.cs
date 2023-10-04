using System;

namespace BetterGenshinImpact.Core.Simulator;

public class MySimulator
{
    public static PostMessageSimulator PostMessage(IntPtr hWnd)
    {
        return new PostMessageSimulator(hWnd);
    }
}