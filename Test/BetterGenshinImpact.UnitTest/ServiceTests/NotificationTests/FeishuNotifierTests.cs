using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using BetterGenshinImpact.Service.Notifier;
using BetterGenshinImpact.Service.Notifier.Exception;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BetterGenshinImpact.UnitTest.ServiceTests.NotificationTests;

/// <summary>
/// 在 HTTP 边界检查飞书卡片通知的 JSON、图片顺序与错误处理，不向外部发送通知。
/// </summary>
public class FeishuNotifierTests
{
    [Fact]
    public async Task SendAsync_WithoutScreenshot_SendsMarkdownCard()
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        using var notifier = new FeishuNotifier(client, "https://example.test/webhook");
        var timestamp = new DateTime(2026, 11, 10, 17, 30, 0);

        await notifier.SendAsync(new BaseNotificationData
        {
            Event = NotificationEvent.DomainEnd.Code,
            Result = NotificationEventResult.Success,
            Message = "任务完成 <ok> & 已领奖",
            Timestamp = timestamp
        });

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/webhook", request.Path);
        using var document = JsonDocument.Parse(request.Body!);
        var root = document.RootElement;
        Assert.Equal("interactive", root.GetProperty("msg_type").GetString());

        var card = root.GetProperty("card");
        Assert.Equal("2.0", card.GetProperty("schema").GetString());
        Assert.Equal("自动秘境结束", card.GetProperty("header").GetProperty("title").GetProperty("content").GetString());
        Assert.Equal("green", card.GetProperty("header").GetProperty("template").GetString());
        Assert.Equal("完成", card.GetProperty("header").GetProperty("text_tag_list")[0].GetProperty("text").GetProperty("content").GetString());

        var elements = card.GetProperty("body").GetProperty("elements");
        Assert.Equal(1, elements.GetArrayLength());
        Assert.Equal("markdown", elements[0].GetProperty("tag").GetString());
        var markdown = elements[0].GetProperty("content").GetString();
        Assert.Contains("2026-11-10 17:30:00", markdown);
        Assert.Contains("任务完成 &#60;ok&#62; &amp; 已领奖", markdown);
    }

    [Fact]
    public async Task SendAsync_WithScreenshot_UploadsImageAndReusesAccessToken()
    {
        using var screenshot = new Image<Rgb24>(32, 32, new Rgb24(12, 34, 56));
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        using var notifier = new FeishuNotifier(client, "https://example.test/webhook", "app-id", "app-secret");
        var content = new BaseNotificationData
        {
            Event = NotificationEvent.TaskError.Code,
            Result = NotificationEventResult.Fail,
            Message = "任务失败",
            Screenshot = screenshot
        };

        await notifier.SendAsync(content);
        await notifier.SendAsync(content);

        Assert.Equal(1, handler.Requests.Count(request => request.Path == "/open-apis/auth/v3/tenant_access_token/internal"));
        Assert.Equal(2, handler.Requests.Count(request => request.Path == "/open-apis/im/v1/images"));
        var webhookRequests = handler.Requests.Where(request => request.Path == "/webhook").ToArray();
        Assert.Equal(2, webhookRequests.Length);

        using var document = JsonDocument.Parse(webhookRequests[0].Body!);
        var card = document.RootElement.GetProperty("card");
        Assert.Equal("red", card.GetProperty("header").GetProperty("template").GetString());
        Assert.Equal("失败", card.GetProperty("header").GetProperty("text_tag_list")[0].GetProperty("text").GetProperty("content").GetString());
        var elements = card.GetProperty("body").GetProperty("elements");
        Assert.Equal(2, elements.GetArrayLength());
        Assert.Equal("img", elements[1].GetProperty("tag").GetString());
        Assert.Equal("img_test", elements[1].GetProperty("img_key").GetString());
    }

    [Fact]
    public async Task SendAsync_WebhookBusinessError_ThrowsNotifierException()
    {
        using var handler = new RecordingHandler
        {
            WebhookResponseBody = "{\"code\":19024,\"msg\":\"Key Words Not Found\"}"
        };
        using var client = new HttpClient(handler);
        using var notifier = new FeishuNotifier(client, "https://example.test/webhook");

        var exception = await Assert.ThrowsAsync<NotifierException>(() => notifier.SendAsync(new BaseNotificationData
        {
            Event = NotificationEvent.Test.Code,
            Message = "测试"
        }));

        Assert.Contains("19024", exception.Message);
        Assert.Contains("Key Words Not Found", exception.Message);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        public string WebhookResponseBody { get; init; } = "{\"code\":0,\"msg\":\"success\"}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(new CapturedRequest { Path = path, Body = body });

            var responseBody = path switch
            {
                "/open-apis/auth/v3/tenant_access_token/internal" =>
                    "{\"code\":0,\"msg\":\"success\",\"tenant_access_token\":\"test-token\",\"expire\":7200}",
                "/open-apis/im/v1/images" =>
                    "{\"code\":0,\"msg\":\"success\",\"data\":{\"image_key\":\"img_test\"}}",
                _ => WebhookResponseBody
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class CapturedRequest
    {
        public string Path { get; init; } = "";

        public string? Body { get; init; }
    }
}
