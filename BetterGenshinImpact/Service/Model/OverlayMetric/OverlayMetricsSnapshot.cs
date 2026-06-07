using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.Service.Model.OverlayMetric;

public sealed record OverlayMetricDisplayItem(string Name, string Value)
{
    public string DisplayText => $"{Name} {Value}";
}

public sealed record OverlayMetricsSnapshot(IReadOnlyList<OverlayMetricDisplayItem> Items)
{
    public static OverlayMetricsSnapshot Empty { get; } = new([]);

    public string CombinedText => string.Join("   ", Items.Select(item => item.DisplayText));
}
