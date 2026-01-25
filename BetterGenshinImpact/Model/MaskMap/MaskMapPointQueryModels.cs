using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Model.MaskMap;

public sealed class MaskMapPointsResult
{
    public IReadOnlyList<MaskMapPointLabel> Labels { get; set; } = Array.Empty<MaskMapPointLabel>();

    public IReadOnlyList<MaskMapPoint> Points { get; set; } = Array.Empty<MaskMapPoint>();
}

public sealed class MaskMapPointInfo
{
    public string Text { get; set; } = string.Empty;

    public string ImageUrl { get; set; } = string.Empty;

    public IReadOnlyList<MaskMapLink> UrlList { get; set; } = Array.Empty<MaskMapLink>();
}

public sealed class MaskMapLink
{
    public string Text { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string DisplayText => string.IsNullOrWhiteSpace(Text) ? "视频攻略" : Text;
}
