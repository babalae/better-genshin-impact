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

/// <summary>Public Yatta v2 quest endpoint; IDs are data.info.id, not story/task IDs.</summary>
public sealed class YattaQuestDataSource : IQuestDataSource
{
    private readonly string query;
    public string SourceKey => "yatta";

    public YattaQuestDataSource(string? versionHash = null)
    {
        if (versionHash is not null && (versionHash.Length is < 1 or > 64 ||
            versionHash.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))))
            throw new ArgumentException("vh must contain only ASCII letters, digits, hyphens or underscores.", nameof(versionHash));
        query = versionHash is null ? "" : "?vh=" + Uri.EscapeDataString(versionHash);
    }

    public Uri GetQuestUri(string questId, string language) =>
        new($"https://gi.yatta.moe/api/v2/{ApiLanguage(language)}/quest/{QuestLoadIdentity.Id(questId)}{query}");

    public Uri GetCatalogUri(string language = "chs") =>
        new($"https://gi.yatta.moe/api/v2/{ApiLanguage(language)}/quest{query}");

    public QuestDocument Parse(ReadOnlyMemory<byte> json, string questId, string language, string sourcePath)
    {
        var id = QuestLoadIdentity.Id(questId);
        var normalizedLanguage = QuestLoadIdentity.Language(language);
        _ = ApiLanguage(normalizedLanguage);
        try
        {
            using var parsed = JsonDocument.Parse(json);
            Validate(parsed.RootElement, id);
            return QuestReader.ReadJson(json, sourcePath, normalizedLanguage);
        }
        catch (JsonException e)
        {
            throw new QuestDataLoadException(new(QuestLoadFailureCode.InvalidJson, "Yatta returned empty or malformed JSON."), e);
        }
    }

    private static string ApiLanguage(string language) => QuestLoadIdentity.Language(language) switch
    {
        "jpn" => "JP", "chs" => "CHS",
        _ => throw new ArgumentException("Yatta quest loading currently supports CHS and JP/jpn.", nameof(language))
    };

    private static void Validate(JsonElement root, string requestedId)
    {
        Object(root, "root");
        var response = Required(root, "response", "root");
        if (!(response.ValueKind == JsonValueKind.Number && response.TryGetInt32(out var code) && code == 200))
            Invalid("response", "Expected successful response=200.");
        var data = Required(root, "data", "root"); Object(data, "data");
        var info = Required(data, "info", "data"); Object(info, "data.info");
        var id = NumericId(Required(info, "id", "data.info"), "data.info.id");
        if (id != requestedId)
            throw new QuestDataLoadException(new(QuestLoadFailureCode.IdentityMismatch, $"Requested chapter {requestedId}, but Yatta returned data.info.id={id}."));
        var stories = Members(Required(data, "storyList", "data"), "data.storyList").ToArray();
        if (stories.Length == 0) Invalid("data.storyList", "Expected at least one main quest definition.");
        foreach (var story in stories)
        {
            Object(story, "storyList entry");
            _ = NumericId(Required(story, "id", "storyList entry"), "storyList entry.id");
            foreach (var task in Members(Required(story, "story", "storyList entry"), "storyList entry.story"))
            {
                Object(task, "story task");
                _ = NumericId(Required(task, "id", "story task"), "story task.id");
                var taskData = Required(task, "taskData", "story task");
                if (taskData.ValueKind == JsonValueKind.Null) continue;
                foreach (var block in Members(taskData, "taskData"))
                {
                    Object(block, "taskData block");
                    _ = Required(block, "initDialog", "taskData block");
                    foreach (var item in Members(Required(block, "items", "taskData block"), "items"))
                    {
                        Object(item, "dialogue node");
                        var type = Required(item, "type", "dialogue node");
                        if (type.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(type.GetString())) Invalid("node.type", "Expected non-empty string.");
                        var standardDialogue = type.GetString() is "SingleDialog" or "MultiDialog";
                        if (item.TryGetProperty("role", out var role) && role.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) Invalid("node.role", "Expected string or null.");
                        // Control nodes can omit text; blank text and empty choice lists are preserved.
                        if (!item.TryGetProperty("text", out var text) || text.ValueKind == JsonValueKind.Null) continue;
                        foreach (var choice in Members(text, "node.text"))
                        {
                            Object(choice, "text choice");
                            if (standardDialogue && Required(choice, "text", "text choice").ValueKind != JsonValueKind.String)
                                Invalid("choice.text", "Dialogue text must be a string (empty string is valid).");
                            if (choice.TryGetProperty("text", out var value) && value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                                Invalid("choice.text", "Expected string or null.");
                        }
                    }
                }
            }
        }
    }

    private static string NumericId(JsonElement value, string context)
    {
        var text = value.ValueKind switch { JsonValueKind.Number => value.GetRawText(), JsonValueKind.String => value.GetString(), _ => null };
        try { return QuestLoadIdentity.Id(text ?? ""); }
        catch (ArgumentException) { Invalid(context, "Expected positive numeric ID."); return ""; }
    }

    private static JsonElement Required(JsonElement obj, string name, string context)
    {
        if (!obj.TryGetProperty(name, out var value)) Invalid(context, $"Missing {name}.");
        return value;
    }

    private static void Object(JsonElement value, string context)
    { if (value.ValueKind != JsonValueKind.Object) Invalid(context, "Expected object."); }

    private static IEnumerable<JsonElement> Members(JsonElement value, string context)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject()) yield return property.Value;
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) yield return item;
        else Invalid(context, "Expected object or array.");
    }

    private static void Invalid(string context, string message) =>
        throw new QuestDataLoadException(new(QuestLoadFailureCode.InvalidSchema, $"Invalid Yatta quest schema at {context}: {message}"));
}
