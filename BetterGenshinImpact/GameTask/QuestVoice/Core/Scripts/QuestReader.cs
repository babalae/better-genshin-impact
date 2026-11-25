using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BetterGenshinImpact.GameTask.QuestVoice.Domain;

public static class QuestReader
{
    private static readonly JsonElement Null = JsonSerializer.SerializeToElement<object?>(null);

    public static QuestDocument ReadJson(ReadOnlyMemory<byte> json, string sourcePath, string language,
        ISpeakerIdentityResolver? speakerResolver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        using var document = JsonDocument.Parse(json);
        return Parse(document.RootElement.Clone(), sourcePath, language, speakerResolver);
    }

    public static QuestDocument Read(string path)
    {
        var source = Path.GetFullPath(path);
        using var json = JsonDocument.Parse(File.ReadAllText(source));
        var raw = json.RootElement.Clone();
        return Parse(raw, source, Language(source), null);
    }

    private static QuestDocument Parse(JsonElement raw, string source, string language, ISpeakerIdentityResolver? speakerResolver)
    {
        var data = Get(raw, "data"); var info = Get(data, "info"); var chapter = Scalar(Get(info, "id")) ?? "unknown";
        var tasks = new List<QuestTask>(); var nodes = new List<QuestNode>(); var edges = new List<QuestEdge>(); var issues = new List<AuditIssue>();
        var stories = new List<QuestStoryDefinition>();
        if (data.ValueKind != JsonValueKind.Object || info.ValueKind != JsonValueKind.Object || Get(data, "storyList").ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            issues.Add(new("invalid-quest-schema", source, "Expected data.info and data.storyList"));
        foreach (var story in Members(Get(data, "storyList")))
        {
            var storyId = Scalar(Get(story.Value, "id")) ?? story.Key;
            var definitions = new List<QuestTaskDefinition>();
            foreach (var task in Members(Get(story.Value, "story")))
            {
                var taskId = Scalar(Get(task.Value, "id")) ?? task.Key;
                definitions.Add(new(taskId, Scalar(Get(task.Value, "title")), task.Value));
                foreach (var block in Members(Get(task.Value, "taskData")))
                {
                    var context = JsonSerializer.Serialize(new[] { source, chapter, story.Key, storyId, task.Key, taskId, block.Key });
                    var init = Get(block.Value, "initDialog");
                    tasks.Add(new(context, storyId, taskId, block.Value, init));
                    var local = new List<QuestNode>(); int ordinal = 0;
                    foreach (var item in Members(Get(block.Value, "items")))
                    {
                        var texts = new List<QuestText>(); int option = 0;
                        foreach (var text in Members(Get(item.Value, "text")))
                        {
                            option++;
                            texts.Add(new(option, Scalar(Get(text.Value, "text")) ?? "", text.Value, Get(text.Value, "next")));
                        }
                        var key = context + "/" + Uri.EscapeDataString(item.Key) + "/" + ordinal++;
                        local.Add(new(key, source, chapter, storyId, taskId, context, item.Key,
                            Scalar(Get(item.Value, "type")) ?? "", Scalar(Get(item.Value, "role")) ?? "", item.Value, texts));
                    }
                    nodes.AddRange(local);
                    if (TryTarget(init, out var initId) && initId != null && !local.Any(n => n.Id == initId))
                        issues.Add(new("missing-init-dialog", context, $"initDialog {initId} has no local node"));
                    foreach (var node in local)
                    foreach (var text in node.Texts)
                    {
                        if (!TryTargets(text.Next, out var targets))
                        {
                            edges.Add(new(node.Key, text.OptionNumber, null, null, "unknown-next", text.Next));
                            issues.Add(new("unknown-next", node.Key, $"Option {text.OptionNumber}: {text.Next.GetRawText()}"));
                            continue;
                        }
                        foreach (var target in targets)
                        {
                            var matches = local.Where(n => n.Id == target).ToArray();
                            var status = target == null ? "terminal" : matches.Length == 0 ? "missing-target" : matches.Length > 1 ? "ambiguous-target" : "resolved";
                            edges.Add(new(node.Key, text.OptionNumber, target, matches.Length == 1 ? matches[0].Key : null, status, text.Next));
                            if (status is "missing-target" or "ambiguous-target") issues.Add(new(status, node.Key, $"Option {text.OptionNumber} next={target}"));
                        }
                    }
                }
            }
            stories.Add(new(storyId, Scalar(Get(Get(story.Value, "info"), "title")), story.Value,
                Array.AsReadOnly(definitions.ToArray())));
        }
        foreach (var group in nodes.GroupBy(n => n.Id).Where(g => g.Count() > 1))
            issues.Add(new("duplicate-id", source, $"Node ID {group.Key}: {group.Count()} distinct occurrences retained"));
        foreach (var group in nodes.SelectMany(n => n.Texts.Select(t => (n.Key, t.Text))).Where(t => t.Text.Length > 0).GroupBy(t => t.Text).Where(g => g.Count() > 1))
            issues.Add(new("duplicate-text", source, $"{group.Count()} occurrences: {group.Key}"));
        return new(source, language, chapter, raw, info, Array.AsReadOnly(tasks.ToArray()),
            QuestSpeakerIdentities.Resolve(nodes, speakerResolver), Array.AsReadOnly(edges.ToArray()), Array.AsReadOnly(issues.ToArray()))
            { Stories = Array.AsReadOnly(stories.ToArray()) };
    }

    public static IReadOnlyList<AuditIssue> CheckChapterPairing(IEnumerable<QuestDocument> documents)
    {
        var docs = documents.ToArray(); var issues = new List<AuditIssue>();
        foreach (var group in docs.GroupBy(d => d.ChapterId))
        {
            if (!group.Any(d => d.Language == "jpn") || !group.Any(d => d.Language == "chs"))
                issues.Add(new("unpaired-chapter", group.Key, "Internal chapter ID lacks jpn/chs pair"));
            foreach (var sameLanguage in group.GroupBy(d => d.Language).Where(g => g.Count() > 1))
                issues.Add(new("duplicate-chapter-language", group.Key, $"Language {sameLanguage.Key} has {sameLanguage.Count()} documents"));
            var numbers = group.Select(d => Prefix(d.SourcePath)).Distinct().ToArray();
            if (numbers.Length > 1)
                issues.Add(new("filename-chapter-mismatch", group.Key, string.Join("; ", group.Select(d => Path.GetFileName(d.SourcePath)))));
            var counterparts = group.ToArray();
            if (counterparts.Length == 2 && counterparts[0].Language != counterparts[1].Language)
            {
                var a = counterparts[0].Nodes.Select(n => (n.StoryId, n.TaskId, n.Id)).ToHashSet();
                var b = counterparts[1].Nodes.Select(n => (n.StoryId, n.TaskId, n.Id)).ToHashSet();
                if (!a.SetEquals(b)) issues.Add(new("chapter-node-mismatch", group.Key, $"Node context sets differ: {a.Count} vs {b.Count}"));
            }
        }
        return issues;
    }

    private static string Language(string path) => Path.GetFileNameWithoutExtension(path).EndsWith("_chs", StringComparison.OrdinalIgnoreCase) ? "chs" : "jpn";
    private static string Prefix(string path) => Regex.Match(Path.GetFileName(path), @"^\d+").Value;
    private static JsonElement Get(JsonElement obj, string name) => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) ? value : Null;
    private static string? Scalar(JsonElement value) => value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), _ => null };
    private static bool TryTarget(JsonElement value, out string? target)
    {
        target = null;
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return true;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) { if (number != 0) target = number.ToString(System.Globalization.CultureInfo.InvariantCulture); return true; }
        if (value.ValueKind == JsonValueKind.String) { var text = value.GetString(); if (!string.IsNullOrEmpty(text) && text != "0" && text != "finish") target = text; return true; }
        return false;
    }
    private static bool TryTargets(JsonElement value, out IReadOnlyList<string?> targets)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            var valid = TryTarget(value, out var target);
            targets = new[] { target }; return valid;
        }
        var found = new List<string?>();
        foreach (var item in value.EnumerateArray())
        {
            if (!TryTarget(item, out var target)) { targets = []; return false; }
            found.Add(target);
        }
        targets = found.Count == 0 ? new string?[] { null } : found;
        return true;
    }
    private static IEnumerable<KeyValuePair<string, JsonElement>> Members(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject()) yield return new(property.Name, property.Value);
        else if (element.ValueKind == JsonValueKind.Array)
        { int index = 0; foreach (var value in element.EnumerateArray()) yield return new((index++).ToString(System.Globalization.CultureInfo.InvariantCulture), value); }
    }
}
