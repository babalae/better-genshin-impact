using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using FormsScreen = System.Windows.Forms.Screen;
using WpfApplication = System.Windows.Application;

namespace BetterGenshinImpact.Service.Hdr;

internal static class DisplayHdrStateReader
{
    private const int ErrorInsufficientBuffer = 122;
    private const int CchDeviceName = 32;

    private static readonly ILogger _logger = App.GetLogger<HdrDetectionService>();

    public static (bool IsKnown, DisplayHdrState State, string? UnknownReason) ReadCurrentDisplayHdrState(nint hWnd = 0)
    {
        string? displayDeviceName = ResolveDisplayDeviceName(hWnd);
        if (string.IsNullOrWhiteSpace(displayDeviceName))
        {
            return (false, DisplayHdrState.Unknown, "无法确定当前显示器，无法判断 Windows 显示 HDR 状态");
        }

        try
        {
            bool? enabled = TryReadAdvancedColorEnabled(displayDeviceName);
            if (!enabled.HasValue)
            {
                return (false, DisplayHdrState.Unknown, "无法将当前显示器映射到 Windows 显示配置，无法判断系统显示 HDR 状态");
            }

            return (true, enabled.Value ? DisplayHdrState.Enabled : DisplayHdrState.Disabled, null);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取当前显示器的 HDR / Advanced Color 状态失败");
            return (false, DisplayHdrState.Unknown, "读取当前显示器的 HDR / Advanced Color 状态失败");
        }
    }

    private static string? ResolveDisplayDeviceName(nint hWnd)
    {
        IntPtr handle = hWnd != 0
            ? (IntPtr)hWnd
            : ResolveMainWindowHandle();

        if (handle != IntPtr.Zero)
        {
            try
            {
                return FormsScreen.FromHandle(handle).DeviceName;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "通过窗口句柄解析显示器设备名失败");
            }
        }

        return FormsScreen.PrimaryScreen?.DeviceName;
    }

    private static IntPtr ResolveMainWindowHandle()
    {
        if (WpfApplication.Current?.MainWindow == null)
        {
            return IntPtr.Zero;
        }

        return new WindowInteropHelper(WpfApplication.Current.MainWindow).Handle;
    }

    private static bool? TryReadAdvancedColorEnabled(string displayDeviceName)
    {
        QueryDisplayConfigFlags flags = QueryDisplayConfigFlags.OnlyActivePaths;
        if (Environment.OSVersion.Version >= new Version(10, 0, 18362))
        {
            flags |= QueryDisplayConfigFlags.VirtualModeAware;
        }

        while (true)
        {
            int result = GetDisplayConfigBufferSizes(flags, out uint pathCount, out uint modeCount);
            if (result != 0)
            {
                throw new Win32Exception(result);
            }

            DisplayConfigPathInfo[] paths = new DisplayConfigPathInfo[pathCount];
            DisplayConfigModeInfo[] modes = new DisplayConfigModeInfo[modeCount];
            result = QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (result == ErrorInsufficientBuffer)
            {
                continue;
            }

            if (result != 0)
            {
                throw new Win32Exception(result);
            }

            bool matchedAnyPath = false;
            bool anyKnownState = false;

            for (int i = 0; i < pathCount; i++)
            {
                DisplayConfigPathInfo path = paths[i];
                if (!TryGetSourceDeviceName(path.SourceInfo.AdapterId, path.SourceInfo.Id, out string? sourceDeviceName))
                {
                    continue;
                }

                if (!string.Equals(sourceDeviceName, displayDeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matchedAnyPath = true;
                if (!TryGetAdvancedColorInfo(path.TargetInfo.AdapterId, path.TargetInfo.Id, out DisplayConfigGetAdvancedColorInfo info))
                {
                    continue;
                }

                anyKnownState = true;
                if (info.AdvancedColorSupported && info.AdvancedColorEnabled && !info.AdvancedColorForceDisabled)
                {
                    return true;
                }
            }

            if (!matchedAnyPath)
            {
                return null;
            }

            return anyKnownState ? false : null;
        }
    }

    private static bool TryGetSourceDeviceName(Luid adapterId, uint sourceId, out string? sourceDeviceName)
    {
        DisplayConfigSourceDeviceName sourceName = new()
        {
            Header = DisplayConfigDeviceInfoHeader.Create(
                DisplayConfigDeviceInfoType.GetSourceName,
                adapterId,
                sourceId),
        };

        int result = DisplayConfigGetDeviceInfo(ref sourceName);
        if (result != 0)
        {
            sourceDeviceName = null;
            return false;
        }

        sourceDeviceName = sourceName.ViewGdiDeviceName?.TrimEnd('\0');
        return !string.IsNullOrWhiteSpace(sourceDeviceName);
    }

    private static bool TryGetAdvancedColorInfo(Luid adapterId, uint targetId, out DisplayConfigGetAdvancedColorInfo info)
    {
        info = new DisplayConfigGetAdvancedColorInfo
        {
            Header = DisplayConfigDeviceInfoHeader.Create(
                DisplayConfigDeviceInfoType.GetAdvancedColorInfo,
                adapterId,
                targetId),
        };

        int result = DisplayConfigGetDeviceInfo(ref info);
        return result == 0;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(
        QueryDisplayConfigFlags flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        QueryDisplayConfigFlags flags,
        ref uint numPathArrayElements,
        [Out] DisplayConfigPathInfo[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [Out] DisplayConfigModeInfo[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName requestPacket);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigGetAdvancedColorInfo requestPacket);

    [Flags]
    private enum QueryDisplayConfigFlags : uint
    {
        OnlyActivePaths = 0x00000002,
        VirtualModeAware = 0x00000010,
    }

    private enum DisplayConfigDeviceInfoType : uint
    {
        GetSourceName = 1,
        GetAdvancedColorInfo = 9,
    }

    private enum DisplayConfigVideoOutputTechnology : uint
    {
        Other = 0xFFFFFFFF,
    }

    private enum DisplayConfigRotation : uint
    {
        Identity = 1,
    }

    private enum DisplayConfigScaling : uint
    {
        Identity = 1,
    }

    private enum DisplayConfigScanLineOrdering : uint
    {
        Unspecified = 0,
    }

    private enum DisplayConfigColorEncoding : uint
    {
        Rgb = 0,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfig2DRegion
    {
        public uint Cx;
        public uint Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointL
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectL
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigVideoSignalInfo
    {
        public ulong PixelRate;
        public DisplayConfigRational HSyncFreq;
        public DisplayConfigRational VSyncFreq;
        public DisplayConfig2DRegion ActiveSize;
        public DisplayConfig2DRegion TotalSize;
        public uint VideoStandard;
        public DisplayConfigScanLineOrdering ScanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigSourceMode
    {
        public uint Width;
        public uint Height;
        public uint PixelFormat;
        public PointL Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigTargetMode
    {
        public DisplayConfigVideoSignalInfo TargetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDesktopImageInfo
    {
        public PointL PathSourceSize;
        public RectL DesktopImageRegion;
        public RectL DesktopImageClip;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct DisplayConfigModeInfo
    {
        [FieldOffset(0)]
        public uint InfoType;

        [FieldOffset(4)]
        public uint Id;

        [FieldOffset(8)]
        public Luid AdapterId;

        [FieldOffset(16)]
        public DisplayConfigTargetMode TargetMode;

        [FieldOffset(16)]
        public DisplayConfigSourceMode SourceMode;

        [FieldOffset(16)]
        public DisplayConfigDesktopImageInfo DesktopImageInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public DisplayConfigVideoOutputTechnology OutputTechnology;
        public DisplayConfigRotation Rotation;
        public DisplayConfigScaling Scaling;
        public DisplayConfigRational RefreshRate;
        public DisplayConfigScanLineOrdering ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public DisplayConfigDeviceInfoType Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;

        public static DisplayConfigDeviceInfoHeader Create(DisplayConfigDeviceInfoType type, Luid adapterId, uint id)
        {
            return new DisplayConfigDeviceInfoHeader
            {
                Type = type,
                Size = type switch
                {
                    DisplayConfigDeviceInfoType.GetSourceName => (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                    DisplayConfigDeviceInfoType.GetAdvancedColorInfo => (uint)Marshal.SizeOf<DisplayConfigGetAdvancedColorInfo>(),
                    _ => 0,
                },
                AdapterId = adapterId,
                Id = id,
            };
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)]
        public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigGetAdvancedColorInfo
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Value;
        public DisplayConfigColorEncoding ColorEncoding;
        public uint BitsPerColorChannel;

        public bool AdvancedColorSupported => (Value & 0x1) != 0;

        public bool AdvancedColorEnabled => (Value & 0x2) != 0;

        public bool AdvancedColorForceDisabled => (Value & 0x8) != 0;
    }
}
