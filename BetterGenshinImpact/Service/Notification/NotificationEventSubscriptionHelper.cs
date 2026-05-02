using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Service.Notification;

public static class NotificationEventSubscriptionHelper
{
    public static IReadOnlyList<string> ParseEventCodes(string? subscribeEventStr)
    {
        if (string.IsNullOrWhiteSpace(subscribeEventStr))
        {
            return Array.Empty<string>();
        }

        var eventCodes = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var eventCode in subscribeEventStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(eventCode))
            {
                continue;
            }

            if (!seen.Add(eventCode))
            {
                continue;
            }

            eventCodes.Add(eventCode);
        }

        return eventCodes;
    }

    public static string NormalizeEventCodes(IEnumerable<string>? eventCodes)
    {
        if (eventCodes == null)
        {
            return string.Empty;
        }

        var normalizedEventCodes = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var eventCode in eventCodes)
        {
            if (string.IsNullOrWhiteSpace(eventCode))
            {
                continue;
            }

            var trimmedCode = eventCode.Trim();
            if (!seen.Add(trimmedCode))
            {
                continue;
            }

            normalizedEventCodes.Add(trimmedCode);
        }

        return string.Join(',', normalizedEventCodes);
    }

    public static bool ShouldSendNotification(string? subscribeEventStr, string? eventCode)
    {
        if (string.IsNullOrWhiteSpace(subscribeEventStr))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(eventCode))
        {
            return false;
        }

        foreach (var subscribeEventCode in ParseEventCodes(subscribeEventStr))
        {
            if (string.Equals(subscribeEventCode, eventCode, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
