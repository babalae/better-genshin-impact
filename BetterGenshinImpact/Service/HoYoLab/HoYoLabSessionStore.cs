using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BetterGenshinImpact.Core.Config;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.HoYoLab;

/// <summary>
/// 从 WebView2 读取到的一条 Cookie
/// </summary>
/// <param name="Name">名称</param>
/// <param name="Value">值</param>
/// <param name="Domain">所属域名</param>
public sealed record HoYoLabCookie(string Name, string Value, string Domain);

/// <summary>
/// 已保存的 HoYoLAB 登录会话
/// </summary>
/// <param name="CookieHeader">请求时使用的 Cookie 请求头</param>
/// <param name="Nickname">账号昵称，仅用于显示</param>
public sealed record HoYoLabSession(string CookieHeader, string? Nickname);

/// <summary>
/// HoYoLAB 登录会话的本地存储。使用 Windows DPAPI（当前用户）加密，不保存密码。
/// </summary>
public class HoYoLabSessionStore
{
    /// <summary>
    /// 默认会话文件位置
    /// </summary>
    public static string DefaultFilePath => Global.Absolute(@"User/MapMask/hoyolab-session.bin");

    /// <summary>
    /// 会话文件路径
    /// </summary>
    private readonly string _filePath;

    /// <summary>
    /// 创建存储
    /// </summary>
    /// <param name="filePath">会话文件路径</param>
    public HoYoLabSessionStore(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>
    /// 由 Cookie 列表生成请求头：只保留 hoyolab.com 及其子域名的 Cookie；没有 ltoken 时视为未登录返回 null
    /// </summary>
    /// <param name="cookies">WebView2 中读取的 Cookie</param>
    /// <returns>Cookie 请求头或 null</returns>
    public static string? BuildCookieHeader(IEnumerable<HoYoLabCookie> cookies)
    {
        var kept = cookies.Where(c => IsHoYoLabDomain(c.Domain)).ToList();
        if (!kept.Any(c => c.Name.StartsWith("ltoken", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return string.Join("; ", kept.Select(c => $"{c.Name}={c.Value}"));
    }

    /// <summary>
    /// 是否为 hoyolab.com 或其子域名
    /// </summary>
    private static bool IsHoYoLabDomain(string domain)
    {
        var d = domain.TrimStart('.');
        return d.Equals("hoyolab.com", StringComparison.OrdinalIgnoreCase)
               || d.EndsWith(".hoyolab.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 加密保存会话
    /// </summary>
    public void Save(HoYoLabSession session)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var plain = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(session));
        File.WriteAllBytes(_filePath, ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser));
    }

    /// <summary>
    /// 读取会话；文件不存在、损坏或不属于当前 Windows 用户时返回 null
    /// </summary>
    public HoYoLabSession? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(_filePath), null, DataProtectionScope.CurrentUser);
            var session = JsonConvert.DeserializeObject<HoYoLabSession>(Encoding.UTF8.GetString(plain));
            return string.IsNullOrWhiteSpace(session?.CookieHeader) ? null : session;
        }
        catch (Exception e) when (e is CryptographicException or JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// 删除会话文件
    /// </summary>
    public void Delete()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }
}
