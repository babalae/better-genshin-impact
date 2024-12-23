using System;
using System.Runtime.InteropServices;

namespace BetterGenshinImpact.Helpers.Device;

public class MouseSpeedSettings
{
    // Import the SystemParametersInfo function from the user32.dll
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SystemParametersInfo(
        uint uiAction, uint uiParam, uint pvParam, uint fWinIni);

    // Constants for SystemParametersInfo function
    private const uint SPI_GETMOUSESPEED = 0x0070;
    private const uint SPI_SETMOUSESPEED = 0x0071;
    

    public static void SetMouseSpeed(uint speed)
    {
        if (speed < 1 || speed > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(speed), "Mouse speed must be between 1 and 20.");
        }

        SystemParametersInfo(SPI_SETMOUSESPEED, 0, speed, 0);
    }
}