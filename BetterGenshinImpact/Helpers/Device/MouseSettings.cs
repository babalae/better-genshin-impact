using System;
using System.Runtime.InteropServices;

namespace BetterGenshinImpact.Helpers.Device;

public class MouseSettings
{
    // Import the SystemParametersInfo function from the user32.dll
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SystemParametersInfo(
        uint uiAction, uint uiParam, ref uint pvParam, uint fWinIni);

    // Constants for SystemParametersInfo function
    private const uint SPI_GETMOUSESPEED = 0x0070;
    private const uint SPI_SETMOUSESPEED = 0x0071;
    private const uint SPI_GETWHEELSCROLLLINES = 0x0068;
    private const uint SPI_SETWHEELSCROLLLINES = 0x0069;

    // Get the current mouse speed
    public static uint GetMouseSpeed()
    {
        uint mouseSpeed = 0;
        SystemParametersInfo(SPI_GETMOUSESPEED, 0, ref mouseSpeed, 0);
        return mouseSpeed;
    }

    // Set the mouse speed
    public  static void SetMouseSpeed(uint speed)
    {
        if (speed < 1 || speed > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(speed), "Mouse speed must be between 1 and 20.");
        }

        SystemParametersInfo(SPI_SETMOUSESPEED, speed, ref speed, 0);
    }

    // Get the current mouse wheel scroll lines
    public  static uint GetMouseWheelScrollLines()
    {
        uint scrollLines = 0;
        SystemParametersInfo(SPI_GETWHEELSCROLLLINES, 0, ref scrollLines, 0);
        return scrollLines;
    }

    // Set the mouse wheel scroll lines
    public  static void SetMouseWheelScrollLines(uint lines)
    {
        SystemParametersInfo(SPI_SETWHEELSCROLLLINES, lines, ref lines, 0);
    }
}