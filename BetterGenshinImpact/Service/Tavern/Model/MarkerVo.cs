using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service.Tavern.Model;

public sealed class MarkerVo
{
    public long? Version { get; set; }

    public long? Id { get; set; }

    public long? CreatorId { get; set; }

    public string? CreateTime { get; set; }

    public long? UpdaterId { get; set; }

    public string? UpdateTime { get; set; }

    public string? MarkerStamp { get; set; }

    public string? MarkerTitle { get; set; }

    public string? Position { get; set; }

    public JToken? ItemList { get; set; }

    public string? Content { get; set; }

    public string? Picture { get; set; }

    public long? MarkerCreatorId { get; set; }

    public long? PictureCreatorId { get; set; }

    public string? VideoPath { get; set; }

    public long? RefreshTime { get; set; }

    public int? HiddenFlag { get; set; }

    public JToken? Extra { get; set; }

    public string? LinkageId { get; set; }
}
