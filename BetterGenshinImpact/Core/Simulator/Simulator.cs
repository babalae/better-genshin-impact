using Windows.Win32.Foundation;

namespace BetterGenshinImpact.Core.Simulator;

public class Simulator
{
    public static PostMessageSimulator PostMessage(HWND hWnd)
    {
        return new PostMessageSimulator(hWnd);
    }
}