using System.Text.Json;

namespace BetterGenshinImpact.Hutao;

internal sealed class HutaoRequest
{
    // DO NOT RENAME: Json convert compatibility
    public HutaoRequestKind Kind { get; set; }

    // DO NOT RENAME: Json convert compatibility
    public JsonElement Data { get; set; }
}