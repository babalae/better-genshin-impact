using System.Net;
using System.Text.Json;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using BetterGenshinImpact.Service.Notifier;

namespace BetterGenshinImpact.UnitTest.ServiceTests.NotificationTests;

public class DomainNotificationDataTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateSuccess_WithoutRewards_ShouldPreserveOriginalMessage(bool empty)
    {
        var notification = DomainNotificationData.CreateSuccess(NotificationEvent.DomainReward,
            "自动秘境奖励领取", empty ? new Dictionary<string, int>() : null);

        Assert.Equal("domain.reward", notification.Event);
        Assert.Equal(NotificationEventResult.Success, notification.Result);
        Assert.Equal("自动秘境奖励领取", notification.Message);
        Assert.Null(notification.Data);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateSuccess_WithRewards_ShouldIncludeTextAndStructuredData(bool summary)
    {
        var rewards = new Dictionary<string, int> { ["摩拉"] = 7050, ["勇士的期许"] = 2 };
        var notificationEvent = summary ? NotificationEvent.DomainEnd : NotificationEvent.DomainReward;
        var message = summary ? "自动秘境结束" : "自动秘境奖励领取";

        var notification = DomainNotificationData.CreateSuccess(notificationEvent, message, rewards);

        Assert.Equal(notificationEvent.Code, notification.Event);
        Assert.Equal(NotificationEventResult.Success, notification.Result);
        Assert.Equal(message + Environment.NewLine + "摩拉 x7050, 勇士的期许 x2", notification.Message);
        var data = Assert.IsType<Dictionary<string, int>>(notification.Data);
        Assert.Equal(7050, data["摩拉"]);
        Assert.Equal(2, data["勇士的期许"]);
    }

    [Fact]
    public void CreateSuccess_ShouldSnapshotRewardsBeforeNextRoundOrTask()
    {
        var rewards = new Dictionary<string, int> { ["摩拉"] = 7050 };
        var firstRound = DomainNotificationData.CreateSuccess(NotificationEvent.DomainReward, "自动秘境奖励领取", rewards);
        rewards["摩拉"] += 7050;
        var summary = DomainNotificationData.CreateSuccess(NotificationEvent.DomainEnd, "自动秘境结束", rewards);
        rewards.Clear();

        Assert.Equal(7050, Assert.IsType<Dictionary<string, int>>(firstRound.Data)["摩拉"]);
        Assert.Equal(14100, Assert.IsType<Dictionary<string, int>>(summary.Data)["摩拉"]);
        Assert.Contains("摩拉 x7050", firstRound.Message);
        Assert.Contains("摩拉 x14100", summary.Message);
    }

    [Fact]
    public async Task Webhook_ShouldSendRewardTextAndStructuredData()
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var notifier = new WebhookNotifier(client, new NotificationConfig { WebhookEndpoint = "https://example.test/notify" });
        var notification = DomainNotificationData.CreateSuccess(NotificationEvent.DomainEnd, "自动秘境结束",
            new Dictionary<string, int> { ["摩拉"] = 14100 });

        await notifier.SendAsync(notification);

        using var payload = JsonDocument.Parse(handler.Body!);
        Assert.Equal("domain.end", payload.RootElement.GetProperty("event").GetString());
        Assert.Contains("摩拉 x14100", payload.RootElement.GetProperty("message").GetString());
        Assert.Equal(14100, payload.RootElement.GetProperty("data").GetProperty("摩拉").GetInt32());
    }

    [Fact]
    public async Task OneBot_ShouldSendRewardTextWithoutStructuredDataSupport()
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var notifier = new OneBotNotifier(client, "https://example.test", userId: "12345");
        var notification = DomainNotificationData.CreateSuccess(NotificationEvent.DomainReward, "自动秘境奖励领取",
            new Dictionary<string, int> { ["勇士的期许"] = 2 });

        await notifier.SendAsync(notification);

        using var payload = JsonDocument.Parse(handler.Body!);
        var text = payload.RootElement.GetProperty("message")[0].GetProperty("data").GetProperty("text").GetString();
        Assert.Equal("自动秘境奖励领取" + Environment.NewLine + "勇士的期许 x2", text);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\"}")
            };
        }
    }
}
