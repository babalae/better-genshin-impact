using System.Threading;
using Windows.Win32.Foundation;
using static Windows.Win32.PInvoke;

namespace Vision.Recognition.Helper.Simulator;

public class PostMessageSimulator
{
    private readonly HWND _hWnd;

    public PostMessageSimulator(HWND hWnd)
    {
        _hWnd = hWnd;
    }

    /// <summary>
    /// 指定位置并按下左键
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    public void LeftButtonClick(int x, int y)
    {
        LPARAM p = (y << 16) | x;
        PostMessage(_hWnd, WM_LBUTTONDOWN, default, p);
        Thread.Sleep(100);
        PostMessage(_hWnd, WM_LBUTTONUP, default, p);
    }

    /// <summary>
    /// 默认位置左键按下
    /// </summary>
    public void LeftButtonDown()
    {
        PostMessage(_hWnd, WM_LBUTTONDOWN, default, default);
    }

    /// <summary>
    /// 默认位置左键释放
    /// </summary>
    public void LeftButtonUp()
    {
        PostMessage(_hWnd, WM_LBUTTONUP, default, default);
    }
}