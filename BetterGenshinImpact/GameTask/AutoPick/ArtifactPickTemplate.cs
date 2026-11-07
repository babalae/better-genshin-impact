using BetterGenshinImpact.Core.Recognition;

namespace BetterGenshinImpact.GameTask.AutoPick;

/// <summary>
/// 一条圣遗物名称模板。
/// </summary>
public sealed class ArtifactPickTemplate
{
    public required string ItemName { get; init; }

    public required RecognitionObject Recognition { get; init; }
}
