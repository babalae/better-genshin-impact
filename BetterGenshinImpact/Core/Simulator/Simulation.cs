using Fischless.WindowsInput;
using System;

namespace BetterGenshinImpact.Core.Simulator;

public class Simulation
{
    public static InputSimulator SendInput { get; } = new();

    public static MouseEventSimulator MouseEvent { get; } = new();

    public static PostMessageSimulator PostMessage(IntPtr hWnd)
    {
        return new PostMessageSimulator(hWnd);
    }
}
