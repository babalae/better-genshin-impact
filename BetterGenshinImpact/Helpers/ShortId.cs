using System;
using System.Security.Cryptography;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// 生成可安全用作 Windows 文件名的短 ID；Pulonia 实体 ID、仓库 ID 与临时文件名共用同一字母表。
/// </summary>
public static class ShortId
{
    /// <summary>
    /// 小写 Crockford Base32 字符表；排除容易混淆的 i、l、o、u。
    /// </summary>
    public const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    /// <summary>
    /// 实体 ID 随机部分的默认字符数，对应 80 位随机空间。
    /// </summary>
    public const int DefaultLength = 16;

    /// <summary>
    /// 临时文件名随机部分的字符数。
    /// </summary>
    private const int TemporaryLength = 8;

    /// <summary>
    /// 生成 <c>前缀_随机串</c>；前缀为空时只返回随机串。
    /// </summary>
    /// <param name="prefix">类型前缀，例如 pln、rep；为空时不加前缀</param>
    /// <param name="length">随机部分字符数</param>
    public static string Create(string? prefix, int length = DefaultLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        // 每个字符承载 5 位，向上取整得到需要的随机字节数。
        Span<byte> randomBytes = stackalloc byte[(length * 5 + 7) / 8];
        RandomNumberGenerator.Fill(randomBytes);
        return Compose(prefix, Encode(randomBytes, length));
    }

    /// <summary>
    /// 由确定性输入的摘要生成形态相同的派生 ID，同一输入始终得到同一结果。
    /// </summary>
    /// <param name="prefix">类型前缀</param>
    /// <param name="value">参与派生的原始文本</param>
    /// <param name="length">输出字符数</param>
    public static string FromText(string prefix, string value, int length = DefaultLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, 51);
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Compose(prefix, Encode(hash, length));
    }

    /// <summary>
    /// 检查文本是否为 <c>前缀_随机串</c> 形态，随机部分只能使用 <see cref="Alphabet"/> 中的字符。
    /// </summary>
    /// <param name="value">待检查的文本</param>
    /// <param name="prefix">要求的类型前缀</param>
    /// <param name="length">随机部分字符数</param>
    public static bool IsMatch(string? value, string prefix, int length = DefaultLength)
    {
        if (value is null || value.Length != prefix.Length + 1 + length
            || !value.StartsWith(prefix + "_", StringComparison.Ordinal))
            return false;
        for (var index = prefix.Length + 1; index < value.Length; index++)
        {
            if (!Alphabet.Contains(value[index]))
                return false;
        }
        return true;
    }

    /// <summary>
    /// 生成与目标名无关的同目录临时名，例如 <c>~k3m9x2ab.tmp</c>。
    /// </summary>
    /// <param name="extension">带点的扩展名，默认 .tmp</param>
    public static string NewTemporaryName(string extension = ".tmp")
        => "~" + Create(null, TemporaryLength) + extension;

    /// <summary>
    /// 拼接前缀与随机部分。
    /// </summary>
    private static string Compose(string? prefix, string body)
        => string.IsNullOrEmpty(prefix) ? body : prefix + "_" + body;

    /// <summary>
    /// 按位读取字节并编码为指定数量的 Base32 字符；输入位数不足时抛出异常。
    /// </summary>
    private static string Encode(ReadOnlySpan<byte> bytes, int length)
    {
        if (bytes.Length * 8 < length * 5)
            throw new ArgumentException("输入字节不足以生成指定长度的 ID。", nameof(bytes));
        Span<char> result = stackalloc char[length];
        uint bitBuffer = 0;
        var bitCount = 0;
        var outputIndex = 0;
        foreach (var value in bytes)
        {
            bitBuffer = (bitBuffer << 8) | value;
            bitCount += 8;
            while (bitCount >= 5 && outputIndex < length)
            {
                bitCount -= 5;
                result[outputIndex++] = Alphabet[(int)((bitBuffer >> bitCount) & 31)];
            }
            if (outputIndex == length)
                break;

            // 只保留尚未输出的低位，避免后续左移积累无关数据。
            bitBuffer = bitCount == 0 ? 0 : bitBuffer & ((1u << bitCount) - 1);
        }
        return new string(result);
    }
}
