namespace BetterGenshinImpact.Infrastructure.Network;

/// <summary>
/// 命名 HttpClient。业务代码通过 IHttpClientFactory.CreateClient(name) 获取，不自行 new HttpClient
/// </summary>
public static class HttpClientNames
{
    /// <summary>BetterGI 自身服务：版本检查、公告、兑换码、更新日志。超时 30 秒</summary>
    public const string Default = "bgi-default";

    /// <summary>游戏相关第三方接口：米游社、HoYoLab、空荧酒馆、日志解析。浏览器 UA 与 Referer 由各服务按请求设置。超时 30 秒</summary>
    public const string GameApi = "bgi-game-api";

    /// <summary>图片：地图图标、点位图片、封面、文档图片。超时 15 秒</summary>
    public const string Images = "bgi-images";

    /// <summary>大文件下载：不限总时长，由调用方控制首包与停滞超时；关闭自动解压缩，避免与续传的字节偏移冲突</summary>
    public const string Download = "bgi-download";

    /// <summary>通知推送渠道。超时 30 秒</summary>
    public const string Notification = "bgi-notification";

    /// <summary>JS 脚本的 http.request。关闭自动重定向，由调用方逐跳校验允许列表。超时 60 秒</summary>
    public const string Script = "bgi-script";

    /// <summary>AI 模型提供方。请求带密钥，关闭自动重定向，避免密钥被转发到其他地址。超时 20 秒</summary>
    public const string AiProvider = "bgi-ai-provider";
}
