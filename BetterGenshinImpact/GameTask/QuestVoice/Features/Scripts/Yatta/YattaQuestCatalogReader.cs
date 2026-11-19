using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Domain;
using System.Text.Json;

namespace BetterGenshinImpact.GameTask.QuestVoice.Genshin;

public sealed record YattaChapter(string Id, string Title, JsonElement Raw)
{
    public string Label => $"{Id} · {Title}";
}

public static class YattaQuestCatalogReader
{
    public static IReadOnlyList<YattaChapter> Read(ReadOnlyMemory<byte> raw)
    {
        using var json = JsonDocument.Parse(raw);
        var root = json.RootElement;
        if (root.GetProperty("response").GetInt32() != 200) throw new InvalidDataException("Yatta 目录未返回 response=200。");
        var items = root.GetProperty("data").GetProperty("items");
        if (items.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Yatta 目录缺少 data.items 对象。");
        return items.EnumerateObject().Select(p =>
        {
            var value = p.Value;
            var id = QuestLoadIdentity.Id(value.GetProperty("id").ToString());
            var title = value.TryGetProperty("chapterTitle", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString()! : "（无标题）";
            return new YattaChapter(id, title, value.Clone());
        }).ToArray();
    }
}
