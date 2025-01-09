using System.Windows.Input;

namespace BetterGenshinImpact.Helpers.Device;

using System;
using System.Runtime.InteropServices;

public static class TouchpadManager
{
    // 定义 DEVMODE 结构
    [StructLayout(LayoutKind.Sequential)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;

        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;

        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    // 导入 ChangeDisplaySettingsEx 函数
    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

    private const int DMDO_DEFAULT = 0;
    private const int DMDO_90 = 1;
    private const int DMDO_180 = 2;
    private const int DMDO_270 = 3;
    private const int CDS_UPDATEREGISTRY = 0x01;
    private const int CDS_TEST = 0x02;
    private const int CDS_FULLSCREEN = 0x04;
    private const int CDS_GLOBAL = 0x08;
    private const int CDS_SET_PRIMARY = 0x10;
    private const int CDS_VIDEOPARAMETERS = 0x20;
    private const int CDS_ENABLE_UNSAFE_MODES = 0x100;
    private const int CDS_DISABLE_UNSAFE_MODES = 0x200;
    private const int CDS_RESET = 0x40000000;
    private const int CDS_NORESET = 0x10000000;

    public static void DisableTouchpad()
    {
        DEVMODE devMode = new DEVMODE();
        devMode.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));
        devMode.dmFields = 0x00080000; // DM_DISPLAYORIENTATION
        devMode.dmDisplayOrientation = DMDO_180; // 旋转显示方向

        int result = ChangeDisplaySettingsEx(null, ref devMode, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);

        if (result == 0)
        {
            Console.WriteLine("触控板已禁用。");
        }
        else
        {
            Console.WriteLine("无法禁用触控板，错误代码: " + result);
        }
    }

    [DllImport("user32.dll")]
    public static extern IntPtr GetSystemMetrics(Int32 nIndex);

    const int SM_DIGITIZER = 94;
    const int NID_INTEGRATED_TOUCH = 0x01;
    const int NID_EXTERNAL_TOUCH = 0x02;
    const int NID_INTEGRATED_PEN = 0x04;
    const int NID_EXTERNAL_PEN = 0x08;
    const int NID_MULTI_INPUT = 0x40;
    const int NID_READY = 0x80;

    public static bool IsTouchpadPresent()
    {
        IntPtr digitizerStatus = GetSystemMetrics(SM_DIGITIZER);
        return (digitizerStatus.ToInt32() & NID_INTEGRATED_TOUCH) != 0 ||
               (digitizerStatus.ToInt32() & NID_EXTERNAL_TOUCH) != 0;
    }

    public static bool HasTouchInput()
    {
        bool hasTouchInput = false;
        UIDispatcherHelper.Invoke(() =>
        {
            foreach (TabletDevice tabletDevice in Tablet.TabletDevices)
            {
                //Only detect if it is a touch Screen not how many touches (i.e. Single touch or Multi-touch)
                if (tabletDevice.Type == TabletDeviceType.Touch)
                {
                    hasTouchInput = true;
                }
            }
        });

        return hasTouchInput;
    }

    public static bool HasTouchInput2()
    {
        IntPtr digitizerStatus = GetSystemMetrics(SM_DIGITIZER);
        return (digitizerStatus.ToInt32() & NID_READY) == 0;
    }
}