using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace BetterGenshinImpact.GameTask.QuestVoice.Domain;

public sealed record QuestTaskDefinition(string TaskId, string? Title, JsonElement Raw);
public sealed record QuestStoryDefinition(string StoryId, string? Title, JsonElement Raw,
    IReadOnlyList<QuestTaskDefinition> Tasks);
public static class QuestDocumentExtensions
{
    public static QuestDocument WithTextContext(this QuestDocument document, QuestTextContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);
        return document with
        {
            Nodes = Array.AsReadOnly(document.Nodes.Select(node => node with
            {
                Texts = Array.AsReadOnly(node.Texts.Select(text => text with { Text = text.Template.Render(context) }).ToArray())
            }).ToArray())
        };
    }
}
