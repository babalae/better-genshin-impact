using BetterGenshinImpact.GameTask.Music.Model;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Music.Service;

public sealed partial class MusicScoreParser : IMusicScoreParser
{
    private const double LowestLatencyMilliseconds = 30;
    private const string SupportedKeys = "QWERTYUASDFGHJZXCVBNM";

    public bool CanParse(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".mid", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".midi", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<PerformanceScore> ParseAsync(
        string path,
        string rootFolder,
        CancellationToken cancellationToken)
    {
        try
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? await ParseJsonAsync(path, rootFolder, cancellationToken)
                : await Task.Run(() => ParseMidiFile(path, rootFolder, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            return CreateInvalidScore(path, rootFolder, e.Message);
        }
    }

    private static async Task<PerformanceScore> ParseJsonAsync(
        string path,
        string rootFolder,
        CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken);
        var json = JObject.Parse(text);
        var type = GetString(json, "type", "yuanqin").Trim().ToLowerInvariant();
        var bpm = GetDouble(json, "bpm", 120);
        var ticks = Math.Max(1, (int)Math.Round(GetDouble(json, "ticks", 480)));
        var timeSignature = GetString(json, "time_signature", "4/4");
        var notes = json["notes"] ?? throw new FormatException("曲谱缺少 notes 字段");

        var format = type switch
        {
            "yuanqin" => MusicScoreFormat.YuanQin,
            "midi" => MusicScoreFormat.MidiJson,
            "keyboard" => MusicScoreFormat.Keyboard,
            _ => throw new FormatException($"不支持的 AutoYuanQin 曲谱类型：{type}")
        };

        var timeline = format switch
        {
            MusicScoreFormat.YuanQin => ParseYuanQinTimeline(notes, bpm, timeSignature),
            MusicScoreFormat.MidiJson => ParseMidiJsonTimeline(GetNotesString(notes, format), bpm, ticks),
            MusicScoreFormat.Keyboard => ParseKeyboardTimeline(GetNotesString(notes, format), bpm),
            _ => PerformanceTimeline.Empty
        };

        return new PerformanceScore
        {
            FullPath = path,
            RelativePath = Path.GetRelativePath(rootFolder, path),
            Name = GetString(json, "name", Path.GetFileNameWithoutExtension(path)),
            Author = GetString(json, "author", "未知作者"),
            Instrument = GetString(json, "instrument", "风物之诗琴"),
            Description = GetString(json, "description", "无描述"),
            Composer = GetString(json, "composer", "未知作曲者"),
            Arranger = GetString(json, "arranger", "未知编曲者"),
            Format = format,
            Bpm = bpm,
            TimeSignature = timeSignature,
            TicksPerQuarterNote = ticks,
            SourceTimeline = timeline,
            MappedNoteCount = timeline.Events.Count(x => x.Type == PerformanceEventType.KeyDown)
        };
    }

    private static PerformanceScore ParseMidiFile(
        string path,
        string rootFolder,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var midiFile = MidiFile.Read(path);
        var tempoMap = midiFile.GetTempoMap();
        var notes = new List<MidiNoteData>();
        var tracks = new ObservableCollection<MusicTrackInfo>();
        var trackChunks = midiFile.GetTrackChunks().ToList();

        for (var trackIndex = 0; trackIndex < trackChunks.Count; trackIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = trackChunks[trackIndex];
            var trackNotes = chunk.GetNotes().ToList();
            if (trackNotes.Count == 0)
            {
                continue;
            }

            foreach (var note in trackNotes)
            {
                var start = note.TimeAs<MetricTimeSpan>(tempoMap);
                var length = note.LengthAs<MetricTimeSpan>(tempoMap);
                var startTime = ToTimeSpan(start);
                var lengthTime = ToTimeSpan(length);
                if (lengthTime <= TimeSpan.Zero)
                {
                    lengthTime = TimeSpan.FromMilliseconds(LowestLatencyMilliseconds);
                }

                notes.Add(new MidiNoteData(
                    trackIndex,
                    note.NoteNumber,
                    startTime,
                    startTime + lengthTime));
            }

            var trackName = chunk.Events
                .OfType<SequenceTrackNameEvent>()
                .FirstOrDefault()?.Text;
            tracks.Add(new MusicTrackInfo
            {
                Index = trackIndex,
                Name = string.IsNullOrWhiteSpace(trackName) ? $"轨道 {trackIndex + 1}" : trackName,
                NoteCount = trackNotes.Count,
                MinNoteNumber = trackNotes.Min(x => (int)x.NoteNumber),
                MaxNoteNumber = trackNotes.Max(x => (int)x.NoteNumber)
            });
        }

        return new PerformanceScore
        {
            FullPath = path,
            RelativePath = Path.GetRelativePath(rootFolder, path),
            Name = Path.GetFileNameWithoutExtension(path),
            Author = "MIDI",
            Instrument = "风物之诗琴",
            Description = "标准 MIDI 文件",
            Format = MusicScoreFormat.MidiFile,
            MidiNotes = notes,
            Tracks = tracks
        };
    }

    private static PerformanceTimeline ParseYuanQinTimeline(
        JToken sheet,
        double initialBpm,
        string timeSignature)
    {
        var tokens = sheet.Type switch
        {
            JTokenType.String => ParseYuanQinTokens(
                (sheet.Value<string>() ?? string.Empty)
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty)),
            JTokenType.Array => ParseYuanQinTokens((JArray)sheet),
            _ => throw new FormatException("yuanqin 曲谱的 notes 必须是字符串或音符对象数组")
        };
        var events = new List<PerformanceEvent>();
        var cursor = TimeSpan.Zero;
        var bpm = initialBpm;
        var beatDenominator = ParseBeatDenominator(timeSignature);

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Special == "%")
            {
                bpm = token.Denominator > 0 ? token.Denominator : bpm;
                continue;
            }

            if (token.Special is "^" or "&")
            {
                AddExplicitEvents(events, cursor, token.Keys, token.Special == "^");
                if (token.Special == "&")
                {
                    cursor += TimeSpan.FromMilliseconds(LowestLatencyMilliseconds);
                }

                continue;
            }

            if (TupletSpecialRegex().IsMatch(token.Special))
            {
                var group = new List<YuanQinToken>();
                while (i < tokens.Count)
                {
                    group.Add(tokens[i]);
                    if (tokens[i].Special.EndsWith('$'))
                    {
                        break;
                    }

                    i++;
                }

                if (!group[^1].Special.EndsWith('$'))
                {
                    throw new FormatException("连音缺少 .$ 结束标记");
                }

                var totalDuration = GetNoteDuration(bpm, beatDenominator, token.Denominator);
                var weights = group.Select(x =>
                {
                    var match = TupletSpecialRegex().Match(x.Special);
                    return 1d / double.Parse(match.Groups["display"].Value, CultureInfo.InvariantCulture);
                }).ToList();
                var weightSum = weights.Sum();

                for (var groupIndex = 0; groupIndex < group.Count; groupIndex++)
                {
                    var duration = TimeSpan.FromTicks(
                        (long)Math.Round(totalDuration.Ticks * weights[groupIndex] / weightSum));
                    AddNote(events, cursor, duration, group[groupIndex].Keys);
                    cursor += duration;
                }

                continue;
            }

            var noteDuration = token.Special == "#"
                ? TimeSpan.FromMilliseconds(60000d / bpm / 16d)
                : GetNoteDuration(bpm, beatDenominator, token.Denominator);
            if (token.Special == "*")
            {
                noteDuration = TimeSpan.FromTicks((long)Math.Round(noteDuration.Ticks * 1.5));
            }

            if (token.Special is "none" or "*")
            {
                var ornamentCount = 0;
                for (var next = i + 1; next < tokens.Count && tokens[next].Special == "#"; next++)
                {
                    ornamentCount++;
                }

                var ornamentsDuration = TimeSpan.FromMilliseconds(60000d / bpm / 16d * ornamentCount);
                if (ornamentsDuration < noteDuration)
                {
                    noteDuration -= ornamentsDuration;
                }
            }

            AddNote(events, cursor, noteDuration, token.Keys);
            cursor += noteDuration;
        }

        return new PerformanceTimeline(ApplyRepeatedKeyGap(events), cursor);
    }

    private static List<YuanQinToken> ParseYuanQinTokens(string sheet)
    {
        var tokens = new List<YuanQinToken>();
        var index = 0;
        while (index < sheet.Length)
        {
            if (sheet[index] == '|')
            {
                index++;
                continue;
            }

            string keys;
            if (sheet[index] == '(')
            {
                var end = sheet.IndexOf(')', index + 1);
                if (end < 0)
                {
                    throw new FormatException("和弦缺少右括号");
                }

                keys = sheet[(index + 1)..end];
                index = end + 1;
            }
            else
            {
                keys = sheet[index].ToString();
                index++;
            }

            if (index >= sheet.Length || sheet[index] != '[')
            {
                throw new FormatException($"音符 {keys} 缺少时值");
            }

            var bracketEnd = sheet.IndexOf(']', index + 1);
            if (bracketEnd < 0)
            {
                throw new FormatException($"音符 {keys} 的时值缺少右方括号");
            }

            var value = sheet[(index + 1)..bracketEnd];
            index = bracketEnd + 1;
            if (value is "^" or "&")
            {
                tokens.Add(new YuanQinToken(keys, 0, value));
                continue;
            }

            var separator = value.IndexOf('-');
            var denominatorText = separator >= 0 ? value[..separator] : value;
            var special = separator >= 0 ? value[(separator + 1)..] : "none";
            if (!double.TryParse(denominatorText, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var denominator))
            {
                throw new FormatException($"无法解析音符 {keys} 的时值：{value}");
            }

            tokens.Add(new YuanQinToken(keys, denominator, special));
        }

        return tokens;
    }

    private static List<YuanQinToken> ParseYuanQinTokens(JArray notes)
    {
        var tokens = new List<YuanQinToken>(notes.Count);
        foreach (var item in notes)
        {
            if (item is not JObject note)
            {
                throw new FormatException("yuanqin 音符数组中包含非对象元素");
            }

            var keys = GetString(note, "note", string.Empty);
            if (string.IsNullOrWhiteSpace(keys))
            {
                throw new FormatException("yuanqin 音符对象缺少 note 字段");
            }

            var special = GetString(note, "spl", "none");
            if (special is "^" or "&")
            {
                tokens.Add(new YuanQinToken(keys, 0, special));
                continue;
            }

            if (!double.TryParse(
                    note["type"]?.ToString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var denominator))
            {
                throw new FormatException($"无法解析音符 {keys} 的时值");
            }

            tokens.Add(new YuanQinToken(keys, denominator, special));
        }

        return tokens;
    }

    private static PerformanceTimeline ParseMidiJsonTimeline(string sheet, double initialBpm, int ticks)
    {
        var events = new List<PerformanceEvent>();
        var cursor = TimeSpan.Zero;
        var bpm = initialBpm;
        string previousStatus = string.Empty;
        string previousKeys = string.Empty;
        var previousWasEvent = false;
        var rawParts = sheet.Split(
            '|',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var index = 0; index < rawParts.Length; index++)
        {
            var rawPart = rawParts[index];
            if (rawPart.StartsWith('*'))
            {
                if (!double.TryParse(rawPart[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out bpm)
                    || bpm <= 0)
                {
                    throw new FormatException($"无法解析 MIDI JSON 变速标记：{rawPart}");
                }

                previousWasEvent = false;
                continue;
            }

            var match = MidiJsonEventRegex().Match(rawPart);
            if (!match.Success)
            {
                throw new FormatException($"无法解析 MIDI JSON 事件：{rawPart}");
            }

            var status = match.Groups["status"].Value;
            var keys = match.Groups["keys"].Value;
            var deltaTicks = long.Parse(match.Groups["ticks"].Value, CultureInfo.InvariantCulture);
            var delay = TimeSpan.FromMilliseconds(deltaTicks * 60000d / (bpm * ticks));
            cursor += delay;

            if (status == "D"
                && previousWasEvent
                && previousStatus == "U"
                && previousKeys.Any(keys.Contains)
                && delay.TotalMilliseconds < LowestLatencyMilliseconds)
            {
                cursor += TimeSpan.FromMilliseconds(LowestLatencyMilliseconds);
            }
            else if (status == "U"
                     && keys != "@"
                     && delay.TotalMilliseconds >= LowestLatencyMilliseconds
                     && index + 1 < rawParts.Length)
            {
                var nextMatch = MidiJsonEventRegex().Match(rawParts[index + 1]);
                if (nextMatch.Success
                    && nextMatch.Groups["status"].Value == "D"
                    && nextMatch.Groups["keys"].Value.Any(keys.Contains))
                {
                    cursor -= TimeSpan.FromMilliseconds(LowestLatencyMilliseconds);
                }
            }

            if (keys != "@")
            {
                AddExplicitEvents(events, cursor, keys, status == "D");
            }

            previousStatus = status;
            previousKeys = keys;
            previousWasEvent = true;
        }

        return new PerformanceTimeline(events, cursor);
    }

    private static PerformanceTimeline ParseKeyboardTimeline(string sheet, double bpm)
    {
        var processed = Regex.Replace(sheet, @"/\(([^)]+)\)", "{$1}");
        processed = Regex.Replace(processed, @"/([A-Z])", "{$1}");
        processed = processed.Replace(" ", "0");
        processed = Regex.Replace(processed, @"/\[([^\]]+)\]", "{[$1]}");
        processed = processed.Replace("/", string.Empty).Replace(">", string.Empty);

        var index = 0;
        var nodes = ParseKeyboardNodes(processed, ref index, null);
        var notes = new List<KeyboardNote>();
        var cumulativeBeats = 0d;
        foreach (var node in nodes)
        {
            UnfoldKeyboardNode(node, cumulativeBeats, node.Multiplier, notes);
            cumulativeBeats += node.Multiplier;
        }

        var beatDuration = 60000d / bpm;
        var events = new List<PerformanceEvent>();
        foreach (var note in notes)
        {
            var start = TimeSpan.FromMilliseconds((note.StartBeat + note.OffsetBeat) * beatDuration);
            var duration = TimeSpan.FromMilliseconds(note.DurationBeat * beatDuration);
            AddNote(events, start, duration, note.Key.ToString());
        }

        var totalDuration = TimeSpan.FromMilliseconds(cumulativeBeats * beatDuration);
        return new PerformanceTimeline(ApplyRepeatedKeyGap(events), totalDuration);
    }

    private static List<KeyboardNode> ParseKeyboardNodes(string text, ref int index, char? closing)
    {
        var nodes = new List<KeyboardNode>();
        while (index < text.Length)
        {
            var current = text[index];
            if (closing.HasValue && current == closing.Value)
            {
                index++;
                return nodes;
            }

            KeyboardNode node;
            if (current is '(' or '[' or '{')
            {
                index++;
                var expectedClosing = current switch
                {
                    '(' => ')',
                    '[' => ']',
                    '{' => '}',
                    _ => throw new InvalidOperationException()
                };
                node = new KeyboardNode(current, null, ParseKeyboardNodes(text, ref index, expectedClosing));
            }
            else if (current is ')' or ']' or '}')
            {
                throw new FormatException($"键谱出现不匹配的括号：{current}");
            }
            else if (current == '-')
            {
                if (nodes.Count == 0)
                {
                    throw new FormatException("键谱延音符号前没有音符");
                }

                nodes[^1].Multiplier++;
                index++;
                continue;
            }
            else
            {
                node = new KeyboardNode('\0', current, []);
                index++;
            }

            while (index < text.Length && text[index] == '-')
            {
                node.Multiplier++;
                index++;
            }

            nodes.Add(node);
        }

        if (closing.HasValue)
        {
            throw new FormatException($"键谱缺少右括号：{closing.Value}");
        }

        return nodes;
    }

    private static void UnfoldKeyboardNode(
        KeyboardNode node,
        double startBeat,
        double durationBeat,
        ICollection<KeyboardNote> output)
    {
        if (node.Value.HasValue)
        {
            var key = char.ToUpperInvariant(node.Value.Value);
            if (key != '0' && SupportedKeys.Contains(key))
            {
                output.Add(new KeyboardNote(key, startBeat, 0, durationBeat));
            }

            return;
        }

        var offset = node.Type == '{' ? 0.001 : 0;
        if (node.Type == '[' && node.Children.Count > 0)
        {
            var unit = durationBeat / node.Children.Count;
            for (var i = 0; i < node.Children.Count; i++)
            {
                UnfoldKeyboardNode(node.Children[i], startBeat + i * unit, unit, output);
            }
        }
        else
        {
            foreach (var child in node.Children)
            {
                UnfoldKeyboardNode(child, startBeat + offset, durationBeat, output);
            }
        }
    }

    private static void AddNote(
        ICollection<PerformanceEvent> events,
        TimeSpan start,
        TimeSpan duration,
        string keys)
    {
        if (keys == "@")
        {
            return;
        }

        foreach (var key in keys.Select(char.ToUpperInvariant).Where(SupportedKeys.Contains).Distinct())
        {
            events.Add(new PerformanceEvent(start, key, PerformanceEventType.KeyDown));
            events.Add(new PerformanceEvent(start + duration, key, PerformanceEventType.KeyUp));
        }
    }

    private static void AddExplicitEvents(
        ICollection<PerformanceEvent> events,
        TimeSpan time,
        string keys,
        bool isDown)
    {
        foreach (var key in keys.Select(char.ToUpperInvariant).Where(SupportedKeys.Contains).Distinct())
        {
            events.Add(new PerformanceEvent(
                time,
                key,
                isDown ? PerformanceEventType.KeyDown : PerformanceEventType.KeyUp));
        }
    }

    private static IReadOnlyList<PerformanceEvent> ApplyRepeatedKeyGap(List<PerformanceEvent> source)
    {
        var events = source
            .OrderBy(x => x.Time)
            .ThenBy(x => x.Type == PerformanceEventType.KeyUp ? 0 : 1)
            .ToList();
        var previousDown = new Dictionary<char, TimeSpan>();
        var previousUpIndex = new Dictionary<char, int>();

        for (var i = 0; i < events.Count; i++)
        {
            var item = events[i];
            if (item.Type == PerformanceEventType.KeyDown)
            {
                if (previousUpIndex.TryGetValue(item.Key, out var upIndex)
                    && events[upIndex].Time == item.Time
                    && previousDown.TryGetValue(item.Key, out var downTime))
                {
                    var earlyTime = item.Time - TimeSpan.FromMilliseconds(LowestLatencyMilliseconds);
                    if (earlyTime > downTime)
                    {
                        events[upIndex] = events[upIndex] with { Time = earlyTime };
                    }
                }

                previousDown[item.Key] = item.Time;
            }
            else
            {
                previousUpIndex[item.Key] = i;
            }
        }

        return events
            .OrderBy(x => x.Time)
            .ThenBy(x => x.Type == PerformanceEventType.KeyUp ? 0 : 1)
            .ToList();
    }

    private static TimeSpan GetNoteDuration(double bpm, int beatDenominator, double noteDenominator)
    {
        if (bpm <= 0 || noteDenominator <= 0)
        {
            throw new FormatException("BPM 和音符时值必须大于 0");
        }

        return TimeSpan.FromMilliseconds(60000d / bpm * beatDenominator / noteDenominator);
    }

    private static int ParseBeatDenominator(string timeSignature)
    {
        var parts = timeSignature.Split('/');
        return parts.Length == 2 && int.TryParse(parts[1], out var denominator) && denominator > 0
            ? denominator
            : 4;
    }

    private static TimeSpan ToTimeSpan(MetricTimeSpan time)
    {
        return TimeSpan.FromMicroseconds(time.TotalMicroseconds);
    }

    private static string GetString(JObject json, string propertyName, string fallback)
    {
        var token = json[propertyName];
        return token == null || token.Type == JTokenType.Null
            ? fallback
            : token.ToString();
    }

    private static double GetDouble(JObject json, string propertyName, double fallback)
    {
        var value = GetString(json, propertyName, fallback.ToString(CultureInfo.InvariantCulture));
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
               && result > 0
            ? result
            : fallback;
    }

    private static string GetNotesString(JToken notes, MusicScoreFormat format)
    {
        return notes.Type == JTokenType.String
            ? notes.Value<string>() ?? string.Empty
            : throw new FormatException($"{format} 曲谱的 notes 必须是字符串");
    }

    private static PerformanceScore CreateInvalidScore(string path, string rootFolder, string error)
    {
        return new PerformanceScore
        {
            FullPath = path,
            RelativePath = Path.GetRelativePath(rootFolder, path),
            Name = Path.GetFileNameWithoutExtension(path),
            Format = Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? MusicScoreFormat.YuanQin
                : MusicScoreFormat.MidiFile,
            Error = error
        };
    }

    [GeneratedRegex(@"^(?<status>[DU])(?<keys>[A-Z@]+)(?<ticks>\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex MidiJsonEventRegex();

    [GeneratedRegex(@"^(?<display>\d+)\.(?<end>[36$])$", RegexOptions.CultureInvariant)]
    private static partial Regex TupletSpecialRegex();

    private sealed record YuanQinToken(string Keys, double Denominator, string Special);

    private sealed class KeyboardNode(char type, char? value, List<KeyboardNode> children)
    {
        public char Type { get; } = type;

        public char? Value { get; } = value;

        public List<KeyboardNode> Children { get; } = children;

        public double Multiplier { get; set; } = 1;
    }

    private sealed record KeyboardNote(char Key, double StartBeat, double OffsetBeat, double DurationBeat);
}
