namespace BetterGenshinImpact.Service.Tavern.Model;

public sealed class ItemTypeVo
{
    public long? Version { get; set; }

    public long? Id { get; set; }

    public long? CreatorId { get; set; }

    public long? CreateTime { get; set; }

    public long? UpdaterId { get; set; }

    public long? UpdateTime { get; set; }

    public long? IconId { get; set; }

    public string? Name { get; set; }

    public string? Content { get; set; }

    public long? ParentId { get; set; }

    public bool? IsFinal { get; set; }

    public int? HiddenFlag { get; set; }

    public int? SortIndex { get; set; }
}