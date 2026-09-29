using BetterGenshinImpact.GameTask.Music.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.Music.Service;

public sealed class MusicTimelineBuilder(IInstrumentProfileService profileService) : IMusicTimelineBuilder
{
    public PerformanceTimeline Build(
        PerformanceScore score,
        InstrumentProfile outputProfile,
        int transpose)
    {
        return score.IsMidi
            ? BuildMidiTimeline(score, outputProfile, transpose)
            : BuildKeyTimeline(score, outputProfile, transpose);
    }

    private static PerformanceTimeline BuildMidiTimeline(
        PerformanceScore score,
        InstrumentProfile outputProfile,
        int transpose)
    {
        var enabledTracks = score.Tracks.Where(x => x.IsEnabled).Select(x => x.Index).ToHashSet();
        var selectedNotes = score.MidiNotes
            .Where(x => enabledTracks.Contains(x.TrackIndex))
            .ToList();
        var events = new List<PerformanceEvent>();
        var mappedTotal = 0;

        foreach (var track in score.Tracks)
        {
            track.MappedNoteCount = 0;
        }

        foreach (var note in selectedNotes)
        {
            if (!outputProfile.TryGetKey(note.NoteNumber + transpose, out var key))
            {
                continue;
            }

            mappedTotal++;
            var track = score.Tracks.FirstOrDefault(x => x.Index == note.TrackIndex);
            if (track != null)
            {
                track.MappedNoteCount++;
            }

            events.Add(new PerformanceEvent(note.Start, key, PerformanceEventType.KeyDown));
            events.Add(new PerformanceEvent(note.End, key, PerformanceEventType.KeyUp));
        }

        score.MappedNoteCount = mappedTotal;
        score.RefreshPlayableRatio();
        var duration = selectedNotes.Count == 0 ? TimeSpan.Zero : selectedNotes.Max(x => x.End);
        return new PerformanceTimeline(Normalize(events), duration);
    }

    private PerformanceTimeline BuildKeyTimeline(
        PerformanceScore score,
        InstrumentProfile outputProfile,
        int transpose)
    {
        var sourceProfile = profileService.Find(score.Instrument);
        var isRawPassThrough = ReferenceEquals(sourceProfile, outputProfile) && transpose == 0;
        var events = new List<PerformanceEvent>();
        var mappedDownKeys = new HashSet<(TimeSpan Time, char Key)>();

        foreach (var item in score.SourceTimeline.Events)
        {
            char targetKey;
            if (isRawPassThrough)
            {
                targetKey = item.Key;
            }
            else if (sourceProfile.TryGetNote(item.Key, out var midiNote)
                     && outputProfile.TryGetKey(midiNote + transpose, out targetKey))
            {
                // 已映射到目标乐器
            }
            else
            {
                continue;
            }

            events.Add(item with { Key = targetKey });
            if (item.Type == PerformanceEventType.KeyDown)
            {
                mappedDownKeys.Add((item.Time, item.Key));
            }
        }

        score.MappedNoteCount = mappedDownKeys.Count;
        score.RefreshPlayableRatio();
        return new PerformanceTimeline(Normalize(events), score.SourceTimeline.Duration);
    }

    private static readonly TimeSpan RetriggerGap = TimeSpan.FromMilliseconds(30);

    private static IReadOnlyList<PerformanceEvent> Normalize(IEnumerable<PerformanceEvent> source)
    {
        var sorted = source
            .OrderBy(x => x.Time)
            .ThenBy(x => x.Type == PerformanceEventType.KeyUp ? 0 : 1)
            .ToList();
        var activeCounts = new Dictionary<char, int>();
        var holdStarts = new Dictionary<char, TimeSpan>();
        var result = new List<PerformanceEvent>(sorted.Count);

        foreach (var item in sorted)
        {
            activeCounts.TryGetValue(item.Key, out var count);
            if (item.Type == PerformanceEventType.KeyDown)
            {
                if (count > 0 && holdStarts.TryGetValue(item.Key, out var holdStart))
                {
                    // 同一键的前一个音尚未松开(连奏交叠):必须先补一个松开再按下。
                    // 否则按键传输层会丢弃这次按下——重复音永远发不出声,
                    // 按键还会被一直按住直到整个交叠链结束(实测可达数十秒)。
                    var retriggerUpTime = item.Time - RetriggerGap;
                    result.Add(new PerformanceEvent(
                        retriggerUpTime > holdStart ? retriggerUpTime : item.Time,
                        item.Key,
                        PerformanceEventType.KeyUp));
                }

                result.Add(item);
                holdStarts[item.Key] = item.Time;
                activeCounts[item.Key] = count + 1;
            }
            else
            {
                if (count <= 0)
                {
                    continue;
                }

                activeCounts[item.Key] = count - 1;
                if (count == 1)
                {
                    result.Add(item);
                    holdStarts.Remove(item.Key);
                }
                // count > 1:这个松开属于之前已被重触发闭合的按住,直接吞掉。
            }
        }

        var previousDown = new Dictionary<char, TimeSpan>();
        var previousUpIndex = new Dictionary<char, int>();
        for (var index = 0; index < result.Count; index++)
        {
            var item = result[index];
            if (item.Type == PerformanceEventType.KeyDown)
            {
                if (previousUpIndex.TryGetValue(item.Key, out var upIndex)
                    && result[upIndex].Time == item.Time
                    && previousDown.TryGetValue(item.Key, out var downTime))
                {
                    var earlyTime = item.Time - RetriggerGap;
                    if (earlyTime > downTime)
                    {
                        result[upIndex] = result[upIndex] with { Time = earlyTime };
                    }
                }

                previousDown[item.Key] = item.Time;
            }
            else
            {
                previousUpIndex[item.Key] = index;
            }
        }

        // 注意:这里必须使用稳定排序且不能再按“松开优先”二次排序。
        // 同一时刻的“松开/按下”在插入时已是正确顺序(松开在前);
        // 若再次按类型排序,会把补拍的松开挪到同键上一次按下的前面,
        // 形成“松开、按下、按下”,传输层将吞掉一次触发并可能粘键。
        return result
            .OrderBy(x => x.Time)
            .ToList();
    }
}
