using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier;
using BetterGenshinImpact.Service.Notifier.Exception;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BetterGenshinImpact.UnitTest.ServiceTests.NotificationTests;

/// <summary>
/// 在 HTTP 边界检查 Telegram 通知的真实图片内容和消息顺序，不向外部发送通知。
/// </summary>
public class TelegramNotifierTests
{
    /// <summary>
    /// 高分辨率、高熵截图的 PNG 超限后，应上传符合 sendPhoto 大小限制的图片。
    /// </summary>
    [Theory]
    [InlineData(3840, 2160)]
    [InlineData(6000, 4000)]
    public async Task SendAsync_OversizedScreenshot_UploadsPhotoWithinLimit(int width, int height)
    {
        using var screenshot = CreateNoiseImage(width, height);
        using var originalPng = new MemoryStream();
        await screenshot.SaveAsPngAsync(originalPng);
        Assert.True(originalPng.Length > 10 * 1024 * 1024);
        var originalPixel = screenshot[0, 0];
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        using var notifier = new TelegramNotifier(client, "test-token", "test-chat");

        await notifier.SendAsync(new BaseNotificationData { Message = "截图通知", Screenshot = screenshot });

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/bottest-token/sendPhoto", request.Path);
        Assert.Equal("test-chat", request.ChatId);
        Assert.Equal("截图通知", request.Caption);
        Assert.InRange(request.Photo!.Length, 1, 10 * 1024 * 1024);
        Assert.Equal("JPEG", Image.DetectFormat(request.Photo).Name);
        Assert.Equal("image.jpg", request.FileName);
        using var uploaded = Image.Load<Rgb24>(request.Photo);
        Assert.True(uploaded.Width <= width && uploaded.Height <= height);
        Assert.Equal(width, screenshot.Width);
        Assert.Equal(height, screenshot.Height);
        Assert.Equal(originalPixel, screenshot[0, 0]);
    }

    /// <summary>
    /// 未超限的截图继续使用无损 PNG，并保留原始像素和短文本 caption。
    /// </summary>
    [Fact]
    public async Task SendAsync_SmallScreenshot_PreservesPngAndCaption()
    {
        using var screenshot = new Image<Rgb24>(64, 32, new Rgb24(12, 34, 56));
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        using var notifier = new TelegramNotifier(client, "test-token", "test-chat");

        await notifier.SendAsync(new BaseNotificationData { Message = "test", Screenshot = screenshot });

        var request = Assert.Single(handler.Requests);
        Assert.Equal("PNG", Image.DetectFormat(request.Photo!).Name);
        Assert.Equal("image.png", request.FileName);
        Assert.Equal("test", request.Caption);
        using var uploaded = Image.Load<Rgb24>(request.Photo!);
        Assert.Equal(64, uploaded.Width);
        Assert.Equal(32, uploaded.Height);
        Assert.Equal(screenshot[0, 0], uploaded[0, 0]);
    }

    /// <summary>
    /// 长文本应继续在图片之后单独发送，避免丢失正文或超过 caption 限制。
    /// </summary>
    [Fact]
    public async Task SendAsync_LongMessage_SendsPhotoThenText()
    {
        using var screenshot = new Image<Rgb24>(32, 32);
        var message = new string('a', 1024);
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        using var notifier = new TelegramNotifier(client, "test-token", "test-chat");

        await notifier.SendAsync(new BaseNotificationData { Message = message, Screenshot = screenshot });

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/bottest-token/sendPhoto", handler.Requests[0].Path);
        Assert.Null(handler.Requests[0].Caption);
        Assert.Equal("/bottest-token/sendMessage", handler.Requests[1].Path);
        using var body = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.Equal(message, body.RootElement.GetProperty("text").GetString());
    }

    /// <summary>
    /// 没有截图的通知仍只发送文本。
    /// </summary>
    [Fact]
    public async Task SendAsync_WithoutScreenshot_SendsTextOnly()
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        using var notifier = new TelegramNotifier(client, "test-token", "test-chat");

        await notifier.SendAsync(new BaseNotificationData { Message = "text only" });

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/bottest-token/sendMessage", request.Path);
        Assert.Null(request.Photo);
    }

    /// <summary>
    /// 图片接口返回失败时仍向调用方报告异常，不能把失败当作发送成功。
    /// </summary>
    [Fact]
    public async Task SendAsync_PhotoRequestFails_PropagatesError()
    {
        using var screenshot = new Image<Rgb24>(32, 32);
        using var handler = new RecordingHandler { StatusCode = HttpStatusCode.Forbidden };
        using var client = new HttpClient(handler);
        using var notifier = new TelegramNotifier(client, "test-token", "test-chat");

        await Assert.ThrowsAsync<NotifierException>(() => notifier.SendAsync(
            new BaseNotificationData { Message = "test", Screenshot = screenshot }));

        Assert.Single(handler.Requests);
    }

    /// <summary>
    /// 固定种子的随机 RGB 像素用于稳定复现 PNG 大于 10 MiB 的情况。
    /// </summary>
    private static Image<Rgb24> CreateNoiseImage(int width, int height)
    {
        var pixels = new byte[width * height * 3];
        new Random(42).NextBytes(pixels);
        return Image.LoadPixelData<Rgb24>(pixels, width, height);
    }

    /// <summary>
    /// 保存编码后的实际请求，并模拟 Telegram 的图片大小拒绝条件。
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        /// <summary>按实际发送顺序记录请求。</summary>
        public List<CapturedRequest> Requests { get; } = [];

        /// <summary>未超限时返回的 HTTP 状态码。</summary>
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

        /// <summary>仅拦截网络层，图片编码和通知发送流程使用生产实现。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var captured = new CapturedRequest { Path = request.RequestUri!.AbsolutePath };
            if (request.Content is MultipartFormDataContent multipart)
            {
                foreach (var part in multipart)
                {
                    switch (part.Headers.ContentDisposition!.Name!.Trim('"'))
                    {
                        case "photo":
                            captured.Photo = await part.ReadAsByteArrayAsync(cancellationToken);
                            captured.FileName = part.Headers.ContentDisposition.FileName!.Trim('"');
                            break;
                        case "chat_id":
                            captured.ChatId = await part.ReadAsStringAsync(cancellationToken);
                            break;
                        case "caption":
                            captured.Caption = await part.ReadAsStringAsync(cancellationToken);
                            break;
                    }
                }
            }
            else
            {
                captured.Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            }

            Requests.Add(captured);
            var oversized = captured.Photo?.Length > 10 * 1024 * 1024;
            var status = oversized ? HttpStatusCode.BadRequest : StatusCode;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(status == HttpStatusCode.OK
                    ? "{\"ok\":true,\"result\":{}}"
                    : "{\"ok\":false,\"error_code\":400,\"description\":\"photo rejected\"}", Encoding.UTF8, "application/json")
            };
        }
    }

    /// <summary>实际 HTTP 请求中供断言检查的字段。</summary>
    private sealed class CapturedRequest
    {
        /// <summary>请求路径。</summary>
        public string Path { get; init; } = "";
        /// <summary>图片实际编码字节。</summary>
        public byte[]? Photo { get; set; }
        /// <summary>上传文件名。</summary>
        public string? FileName { get; set; }
        /// <summary>目标聊天标识。</summary>
        public string? ChatId { get; set; }
        /// <summary>图片 caption。</summary>
        public string? Caption { get; set; }
        /// <summary>纯文本请求的 JSON 正文。</summary>
        public string? Body { get; set; }
    }
}
