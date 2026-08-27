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

    /// <summary>
    /// 获取 <c>GetState</c> 对应的数据。
    /// </summary>
    public static HdrDisplayState GetState(nint hWnd)
    {
        try
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
                Gdi32.DISPLAYCONFIG_SOURCE_DEVICE_NAME sourceName;
                try
                {
                    sourceName = User32.DisplayConfigGetDeviceInfo<Gdi32.DISPLAYCONFIG_SOURCE_DEVICE_NAME>(
                        path.sourceInfo.adapterId,
                        path.sourceInfo.id,
                        Gdi32.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME);
                }
                catch
                {
                    // 单条无关显示路径查询失败时继续匹配；最终仍找不到目标则返回 Unknown。
                    continue;
                }

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
                    return IsValidSdrWhiteLevel(whiteLevelNits)
                        ? HdrDisplayState.CreateHdr(CalculateSdrWhiteScale(whiteLevelNits))
                        : HdrDisplayState.HdrWhiteLevelUnavailable;
                }
                catch
                {
                    // HDR 已确认时不能因白电平查询失败退回 B8；保留 FP16 并使用兼容曝光。
                    return HdrDisplayState.HdrWhiteLevelUnavailable;
                }
            }
        }
        catch
        {
            // 未能确认显示状态时由 HDR 捕获启动策略 fail closed，不能伪装成确定的 SDR。
        }

        return HdrDisplayState.Unknown;
    }

    /// <summary>
    /// 判断 <c>IsValidSdrWhiteLevel</c> 所描述的条件是否成立。
    /// </summary>
    private static bool IsValidSdrWhiteLevel(float sdrWhiteLevelNits)
    {
        return float.IsFinite(sdrWhiteLevelNits) && sdrWhiteLevelNits > 0f;
    }

    /// <summary>
    /// 计算 <c>CalculateSdrWhiteScale</c> 对应的结果。
    /// </summary>
    internal static float CalculateSdrWhiteScale(float sdrWhiteLevelNits)
    {
        if (!float.IsFinite(sdrWhiteLevelNits) || sdrWhiteLevelNits <= 0f)
        {
            return FallbackSdrWhiteScale;
        }

        // Windows scRGB 的 1.0 表示 80 nits；除以用户的 SDR white level 可还原标准线性 SDR 白色。
        return SceneReferredSdrWhiteNits / sdrWhiteLevelNits;
    }
}

internal enum HdrDisplayStateKind
{
    // 必须为 0，确保 default(HdrDisplayState) 也不会被误判为确定的 SDR。
    Unknown = 0,
    Sdr = 1,
    Hdr = 2,
    HdrWhiteLevelUnavailable = 3,
}

internal readonly record struct HdrDisplayState(HdrDisplayStateKind Kind, float SdrWhiteScale)
{
    public bool IsKnown => Kind != HdrDisplayStateKind.Unknown;

    public bool IsHdrEnabled => Kind is
        HdrDisplayStateKind.Hdr or HdrDisplayStateKind.HdrWhiteLevelUnavailable;

    public static HdrDisplayState Unknown => new(HdrDisplayStateKind.Unknown, 1f);

    public static HdrDisplayState Sdr => new(HdrDisplayStateKind.Sdr, 1f);

    public static HdrDisplayState HdrWhiteLevelUnavailable =>
        new(HdrDisplayStateKind.HdrWhiteLevelUnavailable, HdrDisplayInformation.FallbackSdrWhiteScale);

    /// <summary>
    /// 创建 <c>CreateHdr</c> 对应的对象或资源。
    /// </summary>
    public static HdrDisplayState CreateHdr(float sdrWhiteScale) =>
        new(HdrDisplayStateKind.Hdr, sdrWhiteScale);
}
