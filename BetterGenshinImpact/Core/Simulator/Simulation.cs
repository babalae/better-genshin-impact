using System;
using WindowsInput;

namespace BetterGenshinImpact.Core.Simulator;

public class Simulation
{
    [Obsolete]
    public static InputSimulator SendInput { get; } = new();

    public static Fischless.WindowsInput.InputSimulator SendInputEx { get; } = new();

    public static MouseEventSimulator MouseEvent { get; } = new();

    public static PostMessageSimulator PostMessage(IntPtr hWnd)
    {
        return new PostMessageSimulator(hWnd);
    }
}
