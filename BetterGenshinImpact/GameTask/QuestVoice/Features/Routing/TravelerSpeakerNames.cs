using System;
using System.Collections.Generic;
using System.Text;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;

/// <summary>
/// 原神剧情里的旅行者发言人标签（包括中文、日文和英文来源差异）。
/// 只匹配整项发言人名称，不匹配对白正文，也不按子串匹配“某某旅人”。
/// 空/荧为性别特定角色名，不加入这一通用集合，避免误抢角色声线。
/// </summary>
public static class TravelerSpeakerNames
{
    private static readonly HashSet<string> GenericLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "旅人", "旅行者", "旅者", "主人公",
        "Traveler", "Traveller", "Player",
        "旅人さん", "旅人君"
    };

    /// <summary>判断明确的旅行者称呼是否是独立发言人标签。</summary>
    public static bool IsGeneric(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        try { return GenericLabels.Contains(name.Trim().Normalize(NormalizationForm.FormKC)); }
        catch (ArgumentException) { return false; }
    }
}
