using System;
using System.Security.Cryptography;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 为 Pulonia 持久化实体生成带类型前缀的短稳定 ID。
/// </summary>
public static class PuloniaId
{
    /// <summary>
    /// 小写 Crockford Base32 字符表；排除容易混淆的 i、l、o、u。
    /// </summary>
    private const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    /// <summary>
    /// 每个 ID 使用的随机字节数，对应 80 位随机空间。
    /// </summary>
    private const int RandomByteCount = 10;

    /// <summary>
    /// 80 位随机值编码后的固定字符数。
    /// </summary>
    private const int RandomPartLength = 16;

    /// <summary>
    /// 生成任务计划 ID。
    /// </summary>
    public static string NewPlanId() => Create("pln");

    /// <summary>
    /// 生成任务节点 ID。
    /// </summary>
    public static string NewTaskId() => Create("tsk");

    /// <summary>
    /// 生成共享参数预设 ID。
    /// </summary>
    public static string NewPresetId() => Create("pre");

    /// <summary>
    /// 生成任务触发器 ID。
    /// </summary>
    public static string NewTriggerId() => Create("trg");

    /// <summary>
    /// 使用密码学随机数和小写 Base32 构造可安全用作 Windows 文件名的 ID。
    /// </summary>
    private static string Create(string prefix)
    {
        Span<byte> randomBytes = stackalloc byte[RandomByteCount];
        RandomNumberGenerator.Fill(randomBytes);

        Span<char> result = stackalloc char[prefix.Length + 1 + RandomPartLength];
        prefix.AsSpan().CopyTo(result);
        result[prefix.Length] = '_';

        // 10 字节恰好编码为 16 个 Base32 字符，不需要补位或截断随机数据。
        uint bitBuffer = 0;
        var bitCount = 0;
        var outputIndex = prefix.Length + 1;
        foreach (var value in randomBytes)
        {
            bitBuffer = (bitBuffer << 8) | value;
            bitCount += 8;
            while (bitCount >= 5)
            {
                bitCount -= 5;
                result[outputIndex++] = Alphabet[(int)((bitBuffer >> bitCount) & 31)];
            }

            // 只保留尚未输出的低位，避免后续左移积累无关数据。
            bitBuffer = bitCount == 0 ? 0 : bitBuffer & ((1u << bitCount) - 1);
        }

        return new string(result);
    }
}
