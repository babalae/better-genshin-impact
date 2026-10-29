using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BetterGenshinImpact.Service.Guide;

/// <summary>
/// 识别出的可在小窗中打开的链接
/// </summary>
/// <param name="Url">规范化后的打开地址</param>
/// <param name="IsVideo">是否为已知视频网站的视频页</param>
/// <param name="IsBilibili">是否为 B 站链接</param>
public sealed record GuideLink(string Url, bool IsVideo, bool IsBilibili);

/// <summary>
/// 悬浮小窗的链接识别：剪贴板、地址框、地图点位链接的识别与规范化，以及视频标识（见设计文档 7.3、9.4、10.1 节）
/// </summary>
public static partial class GuideLinkResolver
{
    /// <summary>
    /// 识别剪贴板时允许的最大文本长度，超过时不识别
    /// </summary>
    public const int MaxTextLength = 1000;

    /// <summary>
    /// B 站搜索地址，地址框输入的不是网址时使用
    /// </summary>
    private const string BilibiliSearchUrl = "https://search.bilibili.com/all?keyword=";

    /// <summary>
    /// 规范化时去掉的跟踪参数（全部小写比较）
    /// </summary>
    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "spm_id_from", "vd_source", "from_spmid", "from", "seid", "share_source", "share_medium", "share_plat",
        "share_session_id", "share_tag", "share_from", "bbid", "ts", "unique_k", "buvid", "mid", "up_id",
        "is_story_h5", "plat_id", "timestamp", "launch_id", "session_id",
    };

    /// <summary>
    /// B 站视频页、番剧页
    /// </summary>
    [GeneratedRegex(@"https?://(?:www\.|m\.)?bilibili\.com/(?:video/(?:BV[0-9A-Za-z]{10}|av\d+)|bangumi/play/(?:ep|ss)\d+)[^\s""'<>，。]*", RegexOptions.IgnoreCase)]
    private static partial Regex BilibiliVideoUrlRegex();

    /// <summary>
    /// B 站短链接
    /// </summary>
    [GeneratedRegex(@"https?://(?:b23\.tv|bili2233\.cn)/[0-9A-Za-z]+", RegexOptions.IgnoreCase)]
    private static partial Regex BilibiliShortUrlRegex();

    /// <summary>
    /// 纯文本 BV 号
    /// </summary>
    [GeneratedRegex(@"(?<![0-9A-Za-z])BV[0-9A-Za-z]{10}(?![0-9A-Za-z])")]
    private static partial Regex BvIdRegex();

    /// <summary>
    /// 任意 http、https 网址
    /// </summary>
    [GeneratedRegex(@"https?://[^\s""'<>，。]+", RegexOptions.IgnoreCase)]
    private static partial Regex AnyUrlRegex();

    /// <summary>
    /// 网址路径中的 BV 号
    /// </summary>
    [GeneratedRegex(@"/video/(BV[0-9A-Za-z]{10})", RegexOptions.IgnoreCase)]
    private static partial Regex PathBvRegex();

    /// <summary>
    /// 网址路径中的 av 号
    /// </summary>
    [GeneratedRegex(@"/video/av(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PathAvRegex();

    /// <summary>
    /// 网址路径中的番剧 ep、ss 号
    /// </summary>
    [GeneratedRegex(@"/bangumi/play/(ep|ss)(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PathBangumiRegex();

    /// <summary>
    /// 从一段文本中识别第一个可以打开的链接。
    /// B 站视频、番剧、短链接和纯文本 BV 号总是识别；<paramref name="allowAnyUrl"/> 为 true 时另外接受任意 http、https 网址
    /// </summary>
    /// <param name="text">剪贴板或其他来源的文本</param>
    /// <param name="allowAnyUrl">是否接受任意网址</param>
    /// <param name="link">识别结果</param>
    /// <returns>识别成功时返回 true</returns>
    public static bool TryResolve(string? text, bool allowAnyUrl, out GuideLink link)
    {
        link = null!;
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength)
        {
            return false;
        }

        var match = BilibiliVideoUrlRegex().Match(text);
        if (match.Success)
        {
            link = new GuideLink(Normalize(match.Value), true, true);
            return true;
        }

        match = BilibiliShortUrlRegex().Match(text);
        if (match.Success)
        {
            // 短链接由网页跳转到真实地址，打开后再由页面上报视频标识
            link = new GuideLink(match.Value, true, true);
            return true;
        }

        match = BvIdRegex().Match(text);
        if (match.Success)
        {
            link = new GuideLink($"https://www.bilibili.com/video/{match.Value}/", true, true);
            return true;
        }

        if (!allowAnyUrl)
        {
            return false;
        }

        match = AnyUrlRegex().Match(text);
        if (match.Success && Uri.TryCreate(match.Value, UriKind.Absolute, out var uri))
        {
            link = new GuideLink(Normalize(uri.AbsoluteUri), IsVideoUrl(uri.AbsoluteUri), IsBilibiliHost(uri.Host));
            return true;
        }

        return false;
    }

    /// <summary>
    /// 地址框输入转为打开地址：网址直接打开，像域名的补 https，BV 号打开视频，其他内容按 B 站搜索
    /// </summary>
    /// <param name="input">用户输入</param>
    /// <returns>打开地址；输入为空时返回 null</returns>
    public static string? FromAddressInput(string? input)
    {
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        // B 站分享文本里常带标题等文字，只要包含视频链接或 BV 号就直接打开
        if (TryResolve(text, allowAnyUrl: false, out var link))
        {
            return link.Url;
        }

        if (text.StartsWith("http", StringComparison.OrdinalIgnoreCase) && TryResolve(text, allowAnyUrl: true, out link))
        {
            return link.Url;
        }

        // 没有空格、包含点号，视为省略了协议的网址
        if (!text.Contains(' ') && text.Contains('.') && Uri.TryCreate("https://" + text, UriKind.Absolute, out var uri)
            && uri.Host.Contains('.'))
        {
            return Normalize(uri.AbsoluteUri);
        }

        return BilibiliSearchUrl + Uri.EscapeDataString(text);
    }

    /// <summary>
    /// 是否为已知视频网站的视频页：B 站视频、番剧、短链接，YouTube 视频
    /// </summary>
    public static bool IsVideoUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        if (IsBilibiliHost(host))
        {
            return PathBvRegex().IsMatch(uri.AbsolutePath) || PathAvRegex().IsMatch(uri.AbsolutePath)
                                                           || PathBangumiRegex().IsMatch(uri.AbsolutePath);
        }

        if (host is "b23.tv" or "bili2233.cn" or "youtu.be")
        {
            return uri.AbsolutePath.Length > 1;
        }

        return host.EndsWith("youtube.com", StringComparison.Ordinal)
               && (uri.AbsolutePath.StartsWith("/watch", StringComparison.OrdinalIgnoreCase)
                   || uri.AbsolutePath.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 规范化打开地址：移动版 B 站改为桌面版，去掉跟踪参数与片段；无法解析时原样返回
    /// </summary>
    public static string Normalize(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return url.Trim();
        }

        var builder = new UriBuilder(uri) { Fragment = string.Empty, Scheme = Uri.UriSchemeHttps, Port = -1 };
        if (builder.Host.Equals("m.bilibili.com", StringComparison.OrdinalIgnoreCase))
        {
            builder.Host = "www.bilibili.com";
        }

        var query = ParseQuery(uri.Query)
            .Where(pair => !TrackingParameters.Contains(pair.Key) && !pair.Key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase))
            .ToList();
        builder.Query = string.Join('&', query.Select(pair => pair.Value is null ? pair.Key : $"{pair.Key}={pair.Value}"));
        return builder.Uri.AbsoluteUri;
    }

    /// <summary>
    /// 由网址得到视频标识（见设计文档 10.1 节）。打开网页前查找视频记录时使用；网页加载后以页面上报的标识为准
    /// </summary>
    /// <param name="url">网址</param>
    /// <returns>视频标识；无法解析时返回 null</returns>
    public static string? GetVideoKey(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (IsBilibiliHost(uri.Host))
        {
            var page = 1;
            var p = ParseQuery(uri.Query).FirstOrDefault(pair => pair.Key == "p").Value;
            if (int.TryParse(p, out var parsed) && parsed > 0)
            {
                page = parsed;
            }

            var bv = PathBvRegex().Match(uri.AbsolutePath);
            if (bv.Success)
            {
                return $"bilibili:{bv.Groups[1].Value}:p{page}";
            }

            var av = PathAvRegex().Match(uri.AbsolutePath);
            if (av.Success)
            {
                return $"bilibili:av{av.Groups[1].Value}:p{page}";
            }

            var bangumi = PathBangumiRegex().Match(uri.AbsolutePath);
            if (bangumi.Success)
            {
                return $"bilibili:{bangumi.Groups[1].Value.ToLowerInvariant()}{bangumi.Groups[2].Value}";
            }
        }

        var normalized = Normalize(uri.AbsoluteUri);
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var clean))
        {
            return null;
        }

        // 其他网站：去掉时间定位参数，其余参数排序后作为标识
        var rest = ParseQuery(clean.Query)
            .Where(pair => !pair.Key.Equals("t", StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value is null ? pair.Key : $"{pair.Key}={pair.Value}");
        var query = string.Join('&', rest);
        return $"url:{clean.Host.ToLowerInvariant()}{clean.AbsolutePath}{(query.Length > 0 ? "?" + query : string.Empty)}";
    }

    /// <summary>
    /// 是否为 B 站域名
    /// </summary>
    public static bool IsBilibiliHost(string host) =>
        host.Equals("bilibili.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".bilibili.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 拆分查询字符串，保持原有的编码，不解码参数值
    /// </summary>
    private static IEnumerable<KeyValuePair<string, string?>> ParseQuery(string query)
    {
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = part.IndexOf('=');
            yield return index < 0
                ? new KeyValuePair<string, string?>(part, null)
                : new KeyValuePair<string, string?>(part[..index], part[(index + 1)..]);
        }
    }
}
