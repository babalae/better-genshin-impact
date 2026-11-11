using System;
using System.IO;
using System.Linq;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 实例键：实例的稳定标识，重启后不变。主实例为 <c>primary</c>，桌面分身为 <c>child_session</c>，
/// 云原神实例为 <c>webview:&lt;小写实例名&gt;</c>；实例目录名把 <c>:</c> 换成 <c>-</c>。
/// 只描述键的格式与映射，不读取当前进程的实例上下文。
/// </summary>
public static class PuloniaInstanceKey
{
    /// <summary>主实例的实例键。</summary>
    public const string Primary = "primary";

    /// <summary>桌面分身的实例键；每个 Windows 用户同时只有一个桌面分身，因此不带会话号。</summary>
    public const string ChildSession = "child_session";

    /// <summary>云原神实例键的前缀，后接小写实例名。</summary>
    public const string WebViewPrefix = "webview:";

    /// <summary>云原神实例名的最大长度，与实例名校验一致；实例目录名因此最长 40 个字符。</summary>
    private const int MaxWebViewNameLength = 32;

    /// <summary>
    /// 云原神实例的实例键。
    /// </summary>
    /// <param name="instanceName">已校验的云原神实例名</param>
    /// <returns><c>webview:&lt;小写实例名&gt;</c></returns>
    public static string ForWebView(string instanceName)
    {
        var key = WebViewPrefix + instanceName.Trim().ToLowerInvariant();
        Validate(key);
        return key;
    }

    /// <summary>
    /// 实例键对应的界面名称：主实例、桌面分身或“云原神 · 小写实例名”；无法识别时原样返回。
    /// </summary>
    /// <param name="instanceKey">实例键</param>
    /// <returns>界面显示名称</returns>
    public static string Describe(string instanceKey) => instanceKey switch
    {
        Primary => "主实例",
        ChildSession => "桌面分身",
        _ when instanceKey.StartsWith(WebViewPrefix, StringComparison.Ordinal) => "云原神 · " + instanceKey[WebViewPrefix.Length..],
        _ => instanceKey
    };

    /// <summary>
    /// 实例键对应的实例目录名，位于 <c>User/Pulonia/instances</c> 下。
    /// </summary>
    /// <param name="instanceKey">合法的实例键</param>
    /// <returns>把 <c>:</c> 换成 <c>-</c> 后的目录名</returns>
    public static string ToDirectoryName(string instanceKey)
    {
        Validate(instanceKey);
        return instanceKey.Replace(':', '-');
    }

    /// <summary>
    /// 实例目录名对应的界面名称：主实例、桌面分身或“云原神 · 实例名”；无法识别时原样返回。
    /// </summary>
    /// <param name="directoryName">实例目录名</param>
    /// <returns>界面显示名称</returns>
    public static string DescribeDirectory(string directoryName) => directoryName switch
    {
        Primary => "主实例",
        ChildSession => "桌面分身",
        _ when directoryName.StartsWith("webview-", StringComparison.Ordinal) => "云原神 · " + directoryName["webview-".Length..],
        _ => directoryName
    };

    /// <summary>
    /// 实例目录名对应的实例键：<c>primary</c>、<c>child_session</c> 原样，<c>webview-&lt;名称&gt;</c> 为 <c>webview:&lt;名称&gt;</c>。
    /// 供按实例目录枚举的设置（实例当前账户、在线标记）反查实例键。
    /// </summary>
    /// <param name="directoryName">实例目录名</param>
    /// <returns>合法的实例键；无法识别时为 null</returns>
    public static string? TryFromDirectoryName(string directoryName)
    {
        const string webViewDirectoryPrefix = "webview-";
        var key = directoryName switch
        {
            Primary or ChildSession => directoryName,
            _ when directoryName.StartsWith(webViewDirectoryPrefix, StringComparison.Ordinal)
                => WebViewPrefix + directoryName[webViewDirectoryPrefix.Length..],
            _ => null
        };
        if (key is null)
            return null;
        try
        {
            Validate(key);
            return key;
        }
        catch (PuloniaTaskValidationException)
        {
            return null;
        }
    }

    /// <summary>
    /// 校验实例键格式，避免把非法值拼进文件路径。
    /// </summary>
    /// <param name="instanceKey">待校验的实例键</param>
    /// <exception cref="PuloniaTaskValidationException">格式无效</exception>
    public static void Validate(string? instanceKey)
    {
        if (instanceKey is Primary or ChildSession)
            return;
        if (instanceKey is not null && instanceKey.StartsWith(WebViewPrefix, StringComparison.Ordinal))
        {
            var name = instanceKey[WebViewPrefix.Length..];
            if (name.Length is > 0 and <= MaxWebViewNameLength && name == name.Trim() && name == name.ToLowerInvariant()
                && !name.EndsWith('.') && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.Any(char.IsControl))
                return;
        }
        throw new PuloniaTaskValidationException("instance_key",
            "实例键必须是 primary、child_session 或 webview:<小写实例名>（1—32 个字符，不含路径非法字符）。");
    }
}
