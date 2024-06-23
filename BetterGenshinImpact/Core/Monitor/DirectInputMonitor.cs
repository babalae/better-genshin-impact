using SharpDX.DirectInput;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Monitor;

public class DirectInputMonitor
{
    public void Start()
    {
        var directInput = new DirectInput();
        var device = new Mouse(directInput);

        device.SetCooperativeLevel(IntPtr.Zero, CooperativeLevel.Background | CooperativeLevel.NonExclusive);

        Task.Run(() =>
        {
            while (true)
            {
                device.Acquire();
                MouseState state = device.GetCurrentState();
                Debug.WriteLine($"{state.X} {state.Y} {state.Buttons[0]} {state.Buttons[1]}");
                System.Threading.Thread.Sleep(1000); // 10ms, equivalent to CLOCKS_PER_SEC/100
            }
        });
    }

    // var mouseGuid = Guid.Empty;
    // foreach (var deviceInstance in directInput.GetDevices(DeviceType.Mouse, DeviceEnumerationFlags.AllDevices))
    // {
    //     mouseGuid = deviceInstance.InstanceGuid;
    // }
    // if (mouseGuid == Guid.Empty)
    // {
    //     Debug.WriteLine("No mouse found.");
    //     return;
    // }
}
