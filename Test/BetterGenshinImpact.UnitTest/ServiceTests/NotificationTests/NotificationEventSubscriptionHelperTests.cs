using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;

namespace BetterGenshinImpact.UnitTest.ServiceTests.NotificationTests;

public class NotificationEventSubscriptionHelperTests
{
    [Fact]
    public void ParseEventCodes_ShouldTrimAndDeduplicate()
    {
        var result = NotificationEventSubscriptionHelper.ParseEventCodes(
            " domain.start, task.error ,domain.start,,TASK.ERROR ");

        Assert.Equal(["domain.start", "task.error"], result);
    }

    [Fact]
    public void NormalizeEventCodes_ShouldReturnStableCommaSeparatedString()
    {
        var result = NotificationEventSubscriptionHelper.NormalizeEventCodes(
            [" domain.start ", "", "task.error", "domain.start"]);

        Assert.Equal("domain.start,task.error", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldSendNotification_ShouldTreatEmptySubscriptionAsAll(string? subscribeEventStr)
    {
        var result = NotificationEventSubscriptionHelper.ShouldSendNotification(
            subscribeEventStr,
            NotificationEvent.DomainStart.Code);

        Assert.True(result);
    }

    [Fact]
    public void ShouldSendNotification_ShouldMatchExactEventCode()
    {
        const string subscribeEventStr = "domain.start,task.error";

        Assert.True(NotificationEventSubscriptionHelper.ShouldSendNotification(subscribeEventStr, "domain.start"));
        Assert.False(NotificationEventSubscriptionHelper.ShouldSendNotification(subscribeEventStr, "domain"));
        Assert.False(NotificationEventSubscriptionHelper.ShouldSendNotification(subscribeEventStr, "task"));
        Assert.False(NotificationEventSubscriptionHelper.ShouldSendNotification(subscribeEventStr, "domain.start.extra"));
    }

    [Fact]
    public void GetAll_ShouldReturnStaticNotificationEvents()
    {
        var events = NotificationEvent.GetAll();

        Assert.NotEmpty(events);
        Assert.Contains(events, notificationEvent => notificationEvent.Code == NotificationEvent.Test.Code);
        Assert.Contains(events, notificationEvent => notificationEvent.Code == NotificationEvent.DomainStart.Code);
    }
}
