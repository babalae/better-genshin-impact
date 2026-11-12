using BetterGenshinImpact.GameTask.QuestVoice.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>Read-only continuous manuscript item. Its type drives typography, never HTML or hard-coded theme colors.</summary>
public sealed record QuestVoiceTranscriptRow(
    string Kind, string Title, string Text, string Speaker = "",
    QuestVoiceDialogueRow? Dialogue = null)
{
    public string KindLabel => Kind switch
    {
        "chapter" => "章", "story" => "主任务", "task" => "子任务",
        "choice" => "选项", "hint" => "剧情提示", _ => ""
    };
}

public partial class QuestVoiceStoryWorkspace
{
    partial void OnSelectedTranscriptRowChanged(QuestVoiceTranscriptRow? row)
    {
        SelectedLine = row?.Dialogue;
    }

    /// <summary>Build one flat virtualized reading stream for every loaded chapter, in Yatta's authored order.</summary>
    private void RebuildTranscript()
    {
        var rows = new List<QuestVoiceTranscriptRow>();
        var byNode = Lines.ToDictionary(line => line.Key, line => line, StringComparer.Ordinal);
        var context = new QuestTextContext(_config.TravelerNickname,
            _config.FemaleTraveler ? TravelerGender.Female : TravelerGender.Male);

        foreach (var pair in _loadedPairs)
        {
            var doc = pair.Chs;
            var chapterRows = new List<QuestVoiceTranscriptRow>();
            var shown = new HashSet<string>(StringComparer.Ordinal);
            foreach (var story in doc.Stories)
            {
                var storyNodes = doc.Nodes.Where(node => node.StoryId == story.StoryId).ToArray();
                if (storyNodes.Length == 0) continue;
                var storyRows = new List<QuestVoiceTranscriptRow>();
                foreach (var task in story.Tasks)
                {
                    var taskNodes = storyNodes.Where(node => node.TaskId == task.TaskId).ToArray();
                    if (taskNodes.Length == 0) continue;
                    var taskRows = new List<QuestVoiceTranscriptRow>();
                    foreach (var node in taskNodes)
                    {
                        shown.Add(node.Key);
                        AddTranscriptNode(taskRows, node, byNode, context);
                    }
                    // Do not show task titles that only own empty script/placeholder nodes.
                    if (taskRows.Count == 0) continue;
                    storyRows.Add(new("task", task.Title ?? "未命名子任务", ""));
                    storyRows.AddRange(taskRows);
                }
                if (storyRows.Count == 0) continue;
                chapterRows.Add(new("story", story.Title ?? "未命名主任务", ""));
                chapterRows.AddRange(storyRows);
            }

            // Scripts with no task/story association still need to be visible if meaningful.
            var extras = new List<QuestVoiceTranscriptRow>();
            foreach (var node in doc.Nodes.Where(node => !shown.Contains(node.Key)))
                AddTranscriptNode(extras, node, byNode, context);
            if (extras.Count > 0)
            {
                chapterRows.Add(new("task", "其他剧情节点", ""));
                chapterRows.AddRange(extras);
            }
            if (chapterRows.Count == 0) continue;
            var title = Chapters.FirstOrDefault(c => c.Id == doc.ChapterId)?.Title
                ?? doc.QuestName ?? doc.ChapterId;
            rows.Add(new("chapter", title, "章节"));
            rows.AddRange(chapterRows);
        }

        TranscriptRows = new ObservableCollection<QuestVoiceTranscriptRow>(rows);
        OnPropertyChanged(nameof(TranscriptRows));
    }
    private static void AddTranscriptNode(List<QuestVoiceTranscriptRow> rows, QuestNode node, IReadOnlyDictionary<string, QuestVoiceDialogueRow> byNode,
        QuestTextContext context)
    {
        if (node.Type == "SingleDialog" && byNode.TryGetValue(node.Key, out var dialogue))
        {
            if (!string.IsNullOrWhiteSpace(dialogue.Chinese))
                rows.Add(new("dialogue", "", dialogue.Chinese, dialogue.Speaker, dialogue));
            return;
        }
        if (node.Texts.Count == 0) return;
        var isChoice = node.Texts.Count > 1 ||
            node.Type.Contains("Option", StringComparison.OrdinalIgnoreCase) ||
            node.Type.Contains("Select", StringComparison.OrdinalIgnoreCase) ||
            node.Type.Contains("Multi", StringComparison.OrdinalIgnoreCase);
        foreach (var text in node.Texts)
        {
            var rendered = text.Template.Render(context)?.Trim();
            if (string.IsNullOrWhiteSpace(rendered)) continue;
            rows.Add(new(isChoice ? "choice" : "hint", "",
                rendered, node.Role));
        }
    }
}
