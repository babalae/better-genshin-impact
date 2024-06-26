using BetterGenshinImpact.Core.Recorder;
using SharpDX.DirectInput;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Monitor;

public class DirectInputMonitor : IDisposable
{
    private bool _isRunning = true;

    private readonly Mouse _mouse;

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
                GlobalKeyMouseRecord.Instance.GlobalHookMouseMoveBy(state);
                Thread.Sleep(5); // 10ms, equivalent to CLOCKS_PER_SEC/100
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
