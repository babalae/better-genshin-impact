using BetterGenshinImpact.GameTask.Common.Resin;

namespace BetterGenshinImpact.ViewModel.Resin;

/// <summary>原粹树脂档位选项。</summary>
/// <param name="Tier">档位值。</param>
/// <param name="DisplayName">界面显示名称。</param>
public sealed record ResinTierChoice(OriginalResinTier Tier, string DisplayName);
