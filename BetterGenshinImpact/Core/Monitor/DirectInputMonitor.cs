using BetterGenshinImpact.Core.Recorder;
using SharpDX.DirectInput;
using System;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Monitor;

public class DirectInputMonitor : IDisposable
{
    private bool _isRunning = true;

    private readonly Mouse _mouse;
    
    public static int Interval = 5;

    public DirectInputMonitor()
    {
        var directInput = new DirectInput();
        _mouse = new Mouse(directInput);
        _mouse.SetCooperativeLevel(IntPtr.Zero, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
    }

    public MouseState GetMouseState()
    {
        _mouse.Acquire();
        return _mouse.GetCurrentState();
    }

    public void Start()
    {
        Task.Run(() =>
        {
            while (_isRunning)
            {
                _mouse.Acquire();
                MouseState state = _mouse.GetCurrentState();
                // Debug.WriteLine($"{state.X} {state.Y} {state.Buttons[0]} {state.Buttons[1]}");
                GlobalKeyMouseRecord.Instance.GlobalHookMouseMoveBy(state, Kernel32.GetTickCount());
                Thread.Sleep(Interval); // 10ms, equivalent to CLOCKS_PER_SEC/100
            }
        });
    }

    public void Stop()
    {
        _isRunning = false;
    }

    public void Dispose()
    {
        _mouse.Dispose();
    }
}
