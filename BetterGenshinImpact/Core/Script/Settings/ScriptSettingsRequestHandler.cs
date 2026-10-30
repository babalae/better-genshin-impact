using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Core.Script.Settings;

/// <summary>
/// 处理 HTML 设置页的请求：/__bgi/ 下的设置接口，以及设置页目录中的文件。
/// 只接收方法、路径和请求体，不依赖 WebView2；设置窗口在请求拦截中调用。
/// </summary>
/// <param name="request">本次打开的设置页。</param>
/// <param name="theme">打开窗口时 BetterGI 的主题，light 或 dark。</param>
/// <param name="requestClose">页面请求关闭窗口时调用；页面发生过站内跳转后 window.close() 会被浏览器忽略，需要这个接口。</param>
/// <param name="logger">记录处理失败的原因。</param>
public sealed class ScriptSettingsRequestHandler(
    ScriptSettingsEditRequest request,
    string theme,
    Action requestClose,
    ILogger logger)
{
    /// <summary>保存请求体的大小上限。</summary>
    public const int MaxBodyBytes = 1024 * 1024;

    /// <summary>设置值的嵌套深度上限。</summary>
    public const int MaxDepth = 32;

    /// <summary>本页主机以外的地址（外网、本机服务等）一律使用的响应。</summary>
    public static ScriptSettingsResponse Blocked { get; } = Error(403, "设置页不能访问其他地址。");

    /// <summary>
    /// 处理一次请求。
    /// </summary>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="path">已解码的地址路径。</param>
    /// <param name="body">请求体；没有时为空。</param>
    public ScriptSettingsResponse Handle(string method, string path, Stream? body)
    {
        try
        {
            // 1. 保留地址：读取、保存设置，读取上下文，关闭窗口
            if (path.StartsWith("/" + ScriptSettingsHtmlPage.ReservedDirectory + "/", StringComparison.OrdinalIgnoreCase))
            {
                return (path.ToLowerInvariant(), method) switch
                {
                    ("/__bgi/settings", "GET") => Json(200, request.Load()),
                    ("/__bgi/settings", "POST") => Save(body),
                    ("/__bgi/context", "GET") => Json(200, CreateContext()),
                    ("/__bgi/close", "POST") => Close(),
                    ("/__bgi/settings" or "/__bgi/context" or "/__bgi/close", _) => Error(405, "不支持的请求方法。"),
                    _ => Error(404, "接口不存在。")
                };
            }

            // 2. 其他地址：设置页目录中的文件
            if (method != "GET")
                return Error(405, "不支持的请求方法。");
            var file = request.Page.ResolveFile(path);
            return file is null
                ? Error(404, "文件不存在。")
                : new ScriptSettingsResponse(200, ScriptSettingsHtmlPage.ContentTypes[Path.GetExtension(file)],
                    File.ReadAllBytes(file));
        }
        catch (Exception ex)
        {
            // 日志不记录设置值，设置中可能有账号等信息
            logger.LogError(ex, "脚本设置页请求失败：{Script} {Method} {Path}",
                request.Page.ScriptName, method, path);
            return Error(500, "操作失败，详见 BetterGI 日志。");
        }
    }

    /// <summary>
    /// 解析请求体并整体替换设置值。
    /// </summary>
    private ScriptSettingsResponse Save(Stream? body)
    {
        if (ReadBody(body) is not { } bytes)
            return Error(413, "设置内容不能超过 1 MB。");

        JObject settings;
        try
        {
            // 字符串保持原样，不按日期解析；超过深度或 JSON 后还有多余内容时报错
            using var reader = new JsonTextReader(new StreamReader(new MemoryStream(bytes), Encoding.UTF8))
            {
                MaxDepth = MaxDepth,
                DateParseHandling = DateParseHandling.None
            };
            if (JToken.ReadFrom(reader) is not JObject value || reader.Read())
                return Error(400, "请求体必须是一个 JSON 对象。");
            settings = value;
        }
        catch (JsonException ex)
        {
            return Error(400, "请求体不是合法的 JSON：" + ex.Message);
        }

        try
        {
            request.Save(settings);
        }
        catch (InvalidOperationException ex)
        {
            return Error(409, ex.Message);
        }

        return Json(200, new JObject { ["ok"] = true });
    }

    /// <summary>
    /// 请求关闭窗口；窗口在当前请求返回后关闭。
    /// </summary>
    private ScriptSettingsResponse Close()
    {
        requestClose();
        return Json(200, new JObject { ["ok"] = true });
    }

    /// <summary>
    /// 页面可选读取的上下文。
    /// </summary>
    private JObject CreateContext() => new()
    {
        ["apiVersion"] = 1,
        ["mode"] = request.IsCreation ? "create" : "edit",
        ["host"] = request.Host,
        ["script"] = new JObject { ["name"] = request.Page.ScriptName, ["version"] = request.Page.ScriptVersion },
        ["bgi"] = new JObject { ["version"] = Global.Version },
        ["theme"] = theme,
        ["language"] = CultureInfo.CurrentUICulture.Name
    };

    /// <summary>
    /// 读取请求体；超过上限时返回 null。
    /// </summary>
    private static byte[]? ReadBody(Stream? body)
    {
        if (body is null)
            return [];
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = body.Read(chunk)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>JSON 响应。</summary>
    private static ScriptSettingsResponse Json(int statusCode, JToken value)
        => new(statusCode, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(value.ToString(Formatting.None)));

    /// <summary>错误响应，error 是给用户看的中文说明。</summary>
    private static ScriptSettingsResponse Error(int statusCode, string message)
        => Json(statusCode, new JObject { ["ok"] = false, ["error"] = message });
}

/// <summary>
/// 一次请求的处理结果。
/// </summary>
/// <param name="StatusCode">HTTP 状态码。</param>
/// <param name="ContentType">Content-Type。</param>
/// <param name="Body">响应正文。</param>
public sealed record ScriptSettingsResponse(int StatusCode, string ContentType, byte[] Body)
{
    /// <summary>
    /// 页面只能访问自己的主机，禁止联网、子框架和插件；保留内联脚本与 eval，作者可以直接把脚本写在 HTML 中。
    /// </summary>
    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; font-src 'self' data:; media-src 'self' data: blob:; connect-src 'self'; " +
        "worker-src 'self' blob:; frame-src 'none'; object-src 'none'; base-uri 'self'";

    /// <summary>WebView2 自定义响应使用的响应头文本。</summary>
    public string Headers =>
        $"Content-Type: {ContentType}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n" +
        $"Referrer-Policy: no-referrer\r\nContent-Security-Policy: {ContentSecurityPolicy}";

    /// <summary>状态码对应的说明文字。</summary>
    public string ReasonPhrase => ((HttpStatusCode)StatusCode).ToString();
}
