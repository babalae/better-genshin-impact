using System.Runtime.InteropServices;
using Vanara.PInvoke;

namespace Fischless.GameCapture.Graphics.Helpers;

/// <summary>
/// 读取目标窗口所在显示器的高级颜色状态，并计算 scRGB 到标准 SDR 线性空间的归一化系数。
/// </summary>
internal static class HdrDisplayInformation
{
    // 已确认 HDR 但白电平值异常时，保留旧版固定曝光作为兼容回退。
    internal const float FallbackSdrWhiteScale = 0.25f;
    private const float SceneReferredSdrWhiteNits = 80f;

    public static HdrDisplayState GetState(nint hWnd)
    {
        if (User32.QueryDisplayConfig(
                User32.QDC.QDC_ONLY_ACTIVE_PATHS,
                out var paths,
                out _,
                out _).Failed)
        {
            return HdrDisplayState.Unknown;
        }

        var monitor = User32.MonitorFromWindow(hWnd, User32.MonitorFlags.MONITOR_DEFAULTTONEAREST);
        if (monitor.IsInvalid)
        {
            return HdrDisplayState.Unknown;
        }

        var monitorInfo = new User32.MONITORINFOEX
        {
            cbSize = (uint)Marshal.SizeOf<User32.MONITORINFOEX>()
        };
        if (!User32.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return HdrDisplayState.Unknown;
        }

        foreach (var path in paths)
        {
            var sourceName = User32.DisplayConfigGetDeviceInfo<Gdi32.DISPLAYCONFIG_SOURCE_DEVICE_NAME>(
                path.sourceInfo.adapterId,
                path.sourceInfo.id,
                Gdi32.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME);

            if (!string.Equals(
                    sourceName.viewGdiDeviceName,
                    monitorInfo.szDevice,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var advancedColor = User32.DisplayConfigGetDeviceInfo<Gdi32.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(
                path.targetInfo.adapterId,
                path.targetInfo.id,
                Gdi32.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO);
            var advancedColorFlags = advancedColor.value;
            var isHdrEnabled = advancedColorFlags.HasFlag(
                                   Gdi32.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_VALUE.advancedColorEnabled) &&
                               !advancedColorFlags.HasFlag(
                                   Gdi32.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_VALUE.wideColorEnforced);
            if (!isHdrEnabled)
            {
                return HdrDisplayState.Sdr;
            }

            try
            {
                var whiteLevel = User32.DisplayConfigGetDeviceInfo<Gdi32.DISPLAYCONFIG_SDR_WHITE_LEVEL>(
                    path.targetInfo.adapterId,
                    path.targetInfo.id,
                    Gdi32.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL);
                var whiteLevelNits = whiteLevel.SDRWhiteLevel / 1000f * SceneReferredSdrWhiteNits;
                return float.IsFinite(whiteLevelNits) && whiteLevelNits > 0f
                    ? HdrDisplayState.CreateHdr(CalculateSdrWhiteScale(whiteLevelNits))
                    : HdrDisplayState.HdrWhiteLevelUnavailable;
            }
            catch
            {
                return HdrDisplayState.HdrWhiteLevelUnavailable;
            }
        }

        return HdrDisplayState.Unknown;
    }

    internal static float CalculateSdrWhiteScale(float sdrWhiteLevelNits) =>
        SceneReferredSdrWhiteNits / sdrWhiteLevelNits;
}

internal enum HdrDisplayStateKind
{
    Unknown = 0,
    Sdr = 1,
    Hdr = 2,
    HdrWhiteLevelUnavailable = 3,
}

internal readonly record struct HdrDisplayState(HdrDisplayStateKind Kind, float SdrWhiteScale)
{
    public bool IsKnown => Kind != HdrDisplayStateKind.Unknown;

    public static HdrDisplayState Unknown => new(HdrDisplayStateKind.Unknown, 1f);

    public static HdrDisplayState Sdr => new(HdrDisplayStateKind.Sdr, 1f);

    public static HdrDisplayState HdrWhiteLevelUnavailable =>
        new(HdrDisplayStateKind.HdrWhiteLevelUnavailable, HdrDisplayInformation.FallbackSdrWhiteScale);

    public static HdrDisplayState CreateHdr(float sdrWhiteScale) =>
        new(HdrDisplayStateKind.Hdr, sdrWhiteScale);
}
