using System;
using WindowsInput;

namespace BetterGenshinImpact.Core.Simulator;

public class Simulation
{
    public static PostMessageSimulator PostMessage(IntPtr hWnd)
    {
        return new PostMessageSimulator(hWnd);
    }

    [Obsolete]
    public static InputSimulator SendInput { get; } = new();

    public static Fischless.WindowsInput.InputSimulator SendInputEx { get; } = new();

    public static MouseEventSimulator MouseEvent { get; } = new();
}
