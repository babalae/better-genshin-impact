namespace BetterGenshinImpact.Service.Model.Hutao;

// 与 Snap.Hutao.Remastered 的 UpdateAutomationTaskStepIndex 保持 JSON 形状一致。
internal sealed class UpdateAutomationTaskStepIndex
{
    public required string Id { get; set; }

    public required int Index { get; set; }
}
