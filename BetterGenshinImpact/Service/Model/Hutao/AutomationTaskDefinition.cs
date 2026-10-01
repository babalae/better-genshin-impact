namespace BetterGenshinImpact.Service.Model.Hutao;

// 与 Snap.Hutao.Remastered 的 AutomationTaskDefinition 保持 JSON 形状一致（Id/Name/Description）。
internal class AutomationTaskDefinition
{
    public required string Id { get; set; }

    public required string Name { get; set; }

    public required string Description { get; set; }
}
