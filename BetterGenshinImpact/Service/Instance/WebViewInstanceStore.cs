using BetterGenshinImpact.Core.Config;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace BetterGenshinImpact.Service.Instance;

/// <summary>
/// 云原神网页版实例名的校验与存储。
/// <para>
/// 每个实例对应 <see cref="Root"/> 下的一个同名目录，目录本身就是实例名的唯一存储，同时作为该实例的 WebView2 用户数据目录。
/// 同名实例同一时刻只能运行一个，由命名互斥体保证（持有方为网页版实例进程，见 InstanceBootstrap）。
/// </para>
/// </summary>
public sealed class WebViewInstanceStore
{
    public const int MaxNameLength = 32;

    private static readonly char[] InvalidNameChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    private static readonly HashSet<string> ReservedNames = new(
        new[] { "CON", "PRN", "AUX", "NUL" }
            .Concat(Enumerable.Range(1, 9).Select(i => $"COM{i}"))
            .Concat(Enumerable.Range(1, 9).Select(i => $"LPT{i}")),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 所有网页版实例的数据根目录 <c>WebView2Data/CloudGame</c>。
    /// WebView2Data 本身是 HtmlMask 等功能共用的用户数据目录，子目录 CloudGame 不会被它们使用，各实例的用户数据互相独立
    /// </summary>
    public static string Root => Global.Absolute(Path.Combine("WebView2Data", "CloudGame"));

    /// <summary>
    /// 已创建的实例名，按名称排序
    /// </summary>
    public IReadOnlyList<string> List()
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        return Directory.EnumerateDirectories(Root)
            .Select(Path.GetFileName)
            .Where(name => name is not null && TryNormalizeName(name, out _, out _))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public bool Exists(string name) =>
        List().Any(existing => string.Equals(existing, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 新建实例目录。名称不合法或已存在时抛出 <see cref="ArgumentException"/>，并附带原因
    /// </summary>
    /// <returns>规范化后的实例名</returns>
    public string Create(string name)
    {
        if (!TryNormalizeName(name, out var normalized, out var error))
        {
            throw new ArgumentException(error, nameof(name));
        }

        if (Exists(normalized))
        {
            throw new ArgumentException($"实例「{normalized}」已存在", nameof(name));
        }

        Directory.CreateDirectory(GetDataFolder(normalized));
        return normalized;
    }

    /// <summary>
    /// 删除实例目录（包含登录态等全部 WebView 数据）。实例正在运行时抛出 <see cref="InvalidOperationException"/>
    /// </summary>
    public void Delete(string name)
    {
        if (!TryNormalizeName(name, out var normalized, out var error))
        {
            throw new ArgumentException(error, nameof(name));
        }

        if (IsRunning(normalized))
        {
            throw new InvalidOperationException($"实例「{normalized}」正在运行，无法删除");
        }

        var folder = GetDataFolder(normalized);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// 实例是否正在运行：网页版实例进程启动后一直持有实例名互斥体
    /// </summary>
    public static bool IsRunning(string name)
    {
        if (!Mutex.TryOpenExisting(MutexName(name), out var mutex))
        {
            return false;
        }

        mutex.Dispose();
        return true;
    }

    /// <summary>
    /// 该实例的 WebView2 用户数据目录
    /// </summary>
    public static string GetDataFolder(string name) => Path.Combine(Root, name.Trim());

    /// <summary>
    /// 实例名互斥体。用哈希避开非法字符和大小写差异；Local 命名空间与 Primary 同一 Windows Session
    /// </summary>
    public static string MutexName(string name)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(name.Trim().ToLowerInvariant()));
        return $@"Local\BetterGI.WebView.{Convert.ToHexString(bytes, 0, 8)}";
    }

    /// <summary>
    /// 校验实例名：去掉首尾空白后 1~32 个字符，不含路径非法字符与控制字符，不以 . 结尾，不是 Windows 保留名
    /// </summary>
    public static bool TryNormalizeName(string? name, out string normalized, out string error)
    {
        normalized = name?.Trim() ?? string.Empty;
        error = string.Empty;

        if (normalized.Length == 0)
        {
            error = "实例名不能为空";
        }
        else if (normalized.Length > MaxNameLength)
        {
            error = $"实例名不能超过 {MaxNameLength} 个字符";
        }
        else if (normalized.IndexOfAny(InvalidNameChars) >= 0 || normalized.Any(char.IsControl))
        {
            error = "实例名不能包含 \\ / : * ? \" < > | 或控制字符";
        }
        else if (normalized.EndsWith('.'))
        {
            error = "实例名不能以 . 结尾";
        }
        else if (ReservedNames.Contains(normalized.Split('.')[0]))
        {
            error = $"「{normalized}」是 Windows 保留名，不能作为实例名";
        }

        return error.Length == 0;
    }
}
