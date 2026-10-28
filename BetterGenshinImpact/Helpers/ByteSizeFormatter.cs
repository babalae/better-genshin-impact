using System.Globalization;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// 把字节数格式化为便于阅读的文本，例如 0 B、512 KB、23.4 MB、1.9 GB
/// </summary>
public static class ByteSizeFormatter
{
    /// <summary>
    /// 依次使用的单位
    /// </summary>
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>
    /// 格式化字节数：小于 100 时保留一位小数，否则取整
    /// </summary>
    public static string Format(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = value < 100 ? "0.#" : "0";
        return $"{value.ToString(format, CultureInfo.InvariantCulture)} {Units[unit]}";
    }
}
