namespace Fischless.GameCapture;

public static class CaptureModeExtensions
{
    /// <summary>
    /// 将配置中的捕获模式名称转换为已定义的捕获模式。
    /// </summary>
    /// <param name="modeName">捕获模式名称。</param>
    /// <returns>已定义的捕获模式。</returns>
    /// <exception cref="ArgumentException">名称为空、无法解析或不是已定义的枚举值。</exception>
    public static CaptureModes ToCaptureMode(this string? modeName)
    {
        if (modeName.TryToCaptureMode(out var mode))
        {
            return mode;
        }

        throw new ArgumentException($"未知的截图模式：{modeName}", nameof(modeName));
    }

    /// <summary>
    /// 尝试将配置中的捕获模式名称转换为已定义的捕获模式。
    /// </summary>
    /// <param name="modeName">捕获模式名称。</param>
    /// <param name="mode">转换成功时为捕获模式；失败时为默认值。</param>
    /// <returns>名称能够解析为已定义枚举值时为 <see langword="true"/>。</returns>
    public static bool TryToCaptureMode(this string? modeName, out CaptureModes mode)
    {
        if (Enum.TryParse(modeName, true, out mode) && Enum.IsDefined(mode))
        {
            return true;
        }

        mode = default;
        return false;
    }
}
