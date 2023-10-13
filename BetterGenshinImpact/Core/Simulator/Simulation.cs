using System;
using WindowsInput;

namespace BetterGenshinImpact.Core.Simulator;

public class Simulation
{
    public static PostMessageSimulator PostMessage(IntPtr hWnd)
    {
        return new PostMessageSimulator(hWnd);
    }

    public static InputSimulator SendInput { get; } = new();
}