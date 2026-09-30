using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Windows.Threading;

namespace BetterGenshinImpact.Core.Input.Backends.WebSdk;

/// <summary>
/// 基于 WebView2 的 SDK 调用桥。
/// <para>
/// 宿主需要用 AddScriptToExecuteOnDocumentCreatedAsync 注入 ys-input-inject.js 和 <see cref="BootstrapScript"/>，
/// 两者顺序无要求，分发脚本在调用时才查找 SDK。
/// </para>
/// 本类不持有 WebView2 的生命周期，由宿主创建和释放。
/// </summary>
public sealed class WebView2InputBridge : IWebInputBridge, IDisposable
{
    private const string RequestChannel = "bgi.input";
    private const string ErrorChannel = "bgi.input.error";

    /// <summary>
    /// 页面侧分发脚本：按到达顺序串行调用 window.__ysInputInject 上的函数，只在失败时回报
    /// </summary>
    public const string BootstrapScript = """
        (() => {
          if (window.__bgiInputBridge) return;
          window.__bgiInputBridge = true;
          let queue = Promise.resolve();
          window.chrome.webview.addEventListener("message", ({ data }) => {
            if (!data || data.channel !== "bgi.input") return;
            const { method, args = [] } = data;
            // 串行执行：tapKey、click 等是异步函数，并发执行会打乱按键顺序
            queue = queue
              .then(() => {
                const sdk = window.__ysInputInject;
                if (!sdk || !Object.hasOwn(sdk, method) || typeof sdk[method] !== "function") {
                  throw new Error(`unknown sdk method: ${method}`);
                }
                return sdk[method](...args);
              })
              .catch(e => window.chrome.webview.postMessage({
                channel: "bgi.input.error", method, error: String(e?.message ?? e)
              }));
          });
        })();
        """;

    private static ILogger? _logger;

    private readonly CoreWebView2 _core;
    private readonly Dispatcher _dispatcher;
    private bool _disposed;

    /// <param name="core">已初始化的 CoreWebView2</param>
    /// <param name="dispatcher">CoreWebView2 所在线程的 Dispatcher</param>
    public WebView2InputBridge(CoreWebView2 core, Dispatcher dispatcher)
    {
        _core = core ?? throw new ArgumentNullException(nameof(core));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _core.WebMessageReceived += OnWebMessageReceived;
    }

    private static ILogger Logger => _logger ??= App.GetLogger<WebView2InputBridge>();

    /// <summary>
    /// SDK 调用失败时触发，参数为 (method, error)。由宿主决定是否停止任务，例如 RTC 数据通道断开时
    /// </summary>
    public event Action<string, string>? InvokeFailed;

    public void Invoke(string method, params object[] args)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);
        var json = JsonConvert.SerializeObject(new { channel = RequestChannel, method, args });

        // 不在 UI 线程时只等到消息投递完成，不等页面执行
        if (_dispatcher.CheckAccess())
        {
            Post(json);
        }
        else
        {
            _dispatcher.Invoke(() => Post(json));
        }
    }

    private void Post(string json)
    {
        if (_disposed)
        {
            throw new InvalidOperationException("云原神 WebView2 输入桥已释放");
        }

        _core.PostWebMessageAsJson(json);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JObject message;
        try
        {
            message = JObject.Parse(e.WebMessageAsJson);
        }
        catch (JsonException)
        {
            // 不是 JSON 对象的消息与本桥无关
            return;
        }

        if (!string.Equals(message.Value<string>("channel"), ErrorChannel, StringComparison.Ordinal))
        {
            return;
        }

        var method = message.Value<string>("method") ?? string.Empty;
        var error = message.Value<string>("error") ?? "未知错误";
        Logger.LogWarning("云原神 SDK 调用失败：{Method}，{Error}", method, error);
        InvokeFailed?.Invoke(method, error);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _core.WebMessageReceived -= OnWebMessageReceived;
    }
}
