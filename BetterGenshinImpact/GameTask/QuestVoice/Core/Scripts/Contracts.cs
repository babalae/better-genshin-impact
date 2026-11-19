using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace BetterGenshinImpact.GameTask.QuestVoice.Domain;

public sealed record AuditIssue(string Code, string Context, string Message);
public sealed record WaveInfo(string Path, bool IsValid, ushort? FormatTag, ushort? Channels, uint? SampleRate, uint? ByteRate, ushort? BlockAlign, ushort? BitsPerSample, long DataBytes, double? DurationSeconds, IReadOnlyList<string> Errors);
public sealed record QuestText(int OptionNumber, string Text, JsonElement Raw, JsonElement Next)
{
    public QuestTextTemplate Template => QuestTextTemplate.Parse(Raw.ValueKind == JsonValueKind.Object &&
        Raw.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : Text);
}
public sealed record QuestNode(string Key, string SourcePath, string ChapterId, string StoryId, string TaskId, string TaskContext, string Id, string Type, string Role, JsonElement Raw, IReadOnlyList<QuestText> Texts)
{
    public SpeakerIdentity? Speaker { get; init; }
}
public sealed record QuestTask(string Key, string StoryId, string TaskId, JsonElement Raw, JsonElement InitDialog);
public sealed record QuestEdge(string FromKey, int OptionNumber, string? TargetId, string? TargetKey, string Status, JsonElement RawNext);
public sealed record QuestDocument(string SourcePath, string Language, string ChapterId, JsonElement Raw, JsonElement Info, IReadOnlyList<QuestTask> Tasks, IReadOnlyList<QuestNode> Nodes, IReadOnlyList<QuestEdge> Edges, IReadOnlyList<AuditIssue> Issues)
{
    public string? QuestName => Info.ValueKind == JsonValueKind.Object && Info.TryGetProperty("chapterTitle", out var name) &&
        name.ValueKind == JsonValueKind.String ? name.GetString() : null;
    public IReadOnlyList<QuestStoryDefinition> Stories { get; init; } = [];
    public QuestSourceMetadata? SourceMetadata { get; init; }
}
public sealed record AudioEntry(string Path, string? Id, int? OptionNumber, string Kind, WaveInfo Wave);
