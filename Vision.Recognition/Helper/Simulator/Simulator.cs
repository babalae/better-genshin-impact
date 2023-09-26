using Windows.Win32.Foundation;

namespace Vision.Recognition.Helper.Simulator;

public class Simulator
{
    public static PostMessageSimulator PostMessage(HWND hWnd)
    {
        return new PostMessageSimulator(hWnd);
    }
}