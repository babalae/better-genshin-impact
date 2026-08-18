namespace BetterGenshinImpact.Service.Model.Hutao;

// 与 Snap.Hutao.Remastered 的 AddAutomationTaskStepDefinition 保持 JSON 形状一致。
internal sealed class AddAutomationTaskStepDefinition
{
    public required string Id { get; set; }

    public required string Description { get; set; }
}
