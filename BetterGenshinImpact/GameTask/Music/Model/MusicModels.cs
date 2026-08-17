using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Media;

namespace BetterGenshinImpact.GameTask.Music.Model;

public enum MusicScoreFormat
{
    YuanQin,
    MidiJson,
    Keyboard,
    MidiFile
}

public enum MusicInputMode
{
    BackgroundPostMessage,
    ForegroundSendInput
}

public enum MusicPlaybackMode
{
    Sequential,
    SingleLoop,
    Shuffle
}

public enum MusicPlaybackState
{
    Stopped,
    Playing,
    Paused
}

public enum InstrumentMappingMode
{
    MelodicOctaveFold,
    Exact
}

public enum PerformanceEventType
{
    KeyDown,
    KeyUp
}

public sealed record PerformanceEvent(TimeSpan Time, char Key, PerformanceEventType Type);

public sealed class PerformanceTimeline
{
    public static readonly PerformanceTimeline Empty = new([], TimeSpan.Zero);

    public PerformanceTimeline(IReadOnlyList<PerformanceEvent> events, TimeSpan duration)
    {
        Events = events;
        Duration = duration;
    }

    public IReadOnlyList<PerformanceEvent> Events { get; }

    public TimeSpan Duration { get; }
}

public sealed record MidiNoteData(
    int TrackIndex,
    int NoteNumber,
    TimeSpan Start,
    TimeSpan End);

public partial class MusicTrackInfo : ObservableObject
{
    public int Index { get; init; }

    public string Name { get; init; } = string.Empty;

    public int NoteCount { get; init; }

    public int MinNoteNumber { get; init; }

    public int MaxNoteNumber { get; init; }

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private int _mappedNoteCount;

    public double PlayableRatio => NoteCount == 0 ? 0 : (double)MappedNoteCount / NoteCount;

    public string PlayableRatioText => $"{PlayableRatio:P1}";

    partial void OnMappedNoteCountChanged(int value)
    {
        OnPropertyChanged(nameof(PlayableRatio));
        OnPropertyChanged(nameof(PlayableRatioText));
    }
}

public partial class PerformanceScore : ObservableObject
{
    public string FullPath { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    public string Instrument { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Composer { get; init; } = string.Empty;

    public string Arranger { get; init; } = string.Empty;

    public MusicScoreFormat Format { get; init; }

    public double Bpm { get; init; } = 120;

    public string TimeSignature { get; init; } = "4/4";

    public int TicksPerQuarterNote { get; init; } = 480;

    public string? Error { get; init; }

    public PerformanceTimeline SourceTimeline { get; init; } = PerformanceTimeline.Empty;

    public IReadOnlyList<MidiNoteData> MidiNotes { get; init; } = [];

    public ObservableCollection<MusicTrackInfo> Tracks { get; init; } = [];

    [ObservableProperty]
    private string _outputProfileName = string.Empty;

    [ObservableProperty]
    private int _transpose;

    [ObservableProperty]
    private int _mappedNoteCount;

    [ObservableProperty]
    private ImageSource? _artwork;

    public bool IsValid => string.IsNullOrEmpty(Error);

    public bool IsMidi => Format == MusicScoreFormat.MidiFile;

    public int NoteCount => IsMidi
        ? MidiNotes.Count
        : SourceTimeline.Events.Count(x => x.Type == PerformanceEventType.KeyDown);

    public int ActiveNoteCount => IsMidi
        ? Tracks.Where(x => x.IsEnabled).Sum(x => x.NoteCount)
        : NoteCount;

    public double PlayableRatio => ActiveNoteCount == 0
        ? 0
        : (double)MappedNoteCount / ActiveNoteCount;

    public string PlayableRatioText => $"{PlayableRatio:P1}";

    public TimeSpan Duration => IsMidi
        ? (MidiNotes.Count == 0 ? TimeSpan.Zero : MidiNotes.Max(x => x.End))
        : SourceTimeline.Duration;

    public string DurationText => Duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

    public string FormatName => Format switch
    {
        MusicScoreFormat.YuanQin => "原琴 JSON",
        MusicScoreFormat.MidiJson => "MIDI JSON",
        MusicScoreFormat.Keyboard => "网络键谱",
        MusicScoreFormat.MidiFile => "MIDI",
        _ => Format.ToString()
    };

    public string DisplayTitle => string.IsNullOrWhiteSpace(Name)
        ? System.IO.Path.GetFileNameWithoutExtension(FullPath)
        : Name;

    partial void OnMappedNoteCountChanged(int value)
    {
        OnPropertyChanged(nameof(ActiveNoteCount));
        OnPropertyChanged(nameof(PlayableRatio));
        OnPropertyChanged(nameof(PlayableRatioText));
    }

    public void RefreshPlayableRatio()
    {
        OnPropertyChanged(nameof(ActiveNoteCount));
        OnPropertyChanged(nameof(PlayableRatio));
        OnPropertyChanged(nameof(PlayableRatioText));
    }
}

public partial class InstrumentKeyMapping : ObservableObject
{
    public InstrumentKeyMapping()
    {
    }

    public InstrumentKeyMapping(char key, int midiNote)
    {
        Key = key.ToString();
        MidiNote = midiNote;
    }

    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    private int _midiNote;

    public string NoteName
    {
        get
        {
            string[] names = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"];
            var normalized = Math.Clamp(MidiNote, 0, 127);
            return $"{names[normalized % 12]}{normalized / 12 - 1}";
        }
    }

    partial void OnMidiNoteChanged(int value)
    {
        OnPropertyChanged(nameof(NoteName));
    }
}

public partial class InstrumentProfile : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private InstrumentMappingMode _mappingMode = InstrumentMappingMode.MelodicOctaveFold;

    public ObservableCollection<string> Aliases { get; set; } = [];

    public ObservableCollection<InstrumentKeyMapping> Mappings { get; set; } = [];

    public bool TryGetNote(char key, out int note)
    {
        var mapping = Mappings.FirstOrDefault(x =>
            x.Key.Length == 1 && char.ToUpperInvariant(x.Key[0]) == char.ToUpperInvariant(key));
        if (mapping == null)
        {
            note = 0;
            return false;
        }

        note = mapping.MidiNote;
        return true;
    }

    public bool TryGetKey(int midiNote, out char key)
    {
        if (Mappings.Count == 0)
        {
            key = default;
            return false;
        }

        var targetNote = midiNote;
        if (MappingMode == InstrumentMappingMode.MelodicOctaveFold)
        {
            var min = Mappings.Min(x => x.MidiNote);
            var max = Mappings.Max(x => x.MidiNote);
            while (targetNote < min)
            {
                targetNote += 12;
            }

            while (targetNote > max)
            {
                targetNote -= 12;
            }
        }

        var mapping = Mappings.FirstOrDefault(x => x.MidiNote == targetNote);
        if (mapping?.Key.Length == 1)
        {
            key = char.ToUpperInvariant(mapping.Key[0]);
            return true;
        }

        key = default;
        return false;
    }

    public bool Matches(string? instrumentName)
    {
        if (string.IsNullOrWhiteSpace(instrumentName))
        {
            return false;
        }

        return string.Equals(Name, instrumentName, StringComparison.OrdinalIgnoreCase)
               || Aliases.Any(x => string.Equals(x, instrumentName, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class PlaybackSnapshot
{
    public MusicPlaybackState State { get; init; } = MusicPlaybackState.Stopped;

    public TimeSpan Position { get; init; }

    public TimeSpan Duration { get; init; }

    public double Speed { get; init; } = 1.0;

    public string TrackName { get; init; } = string.Empty;

    public int QueueIndex { get; init; } = -1;
}

public sealed class MusicPlaybackOptions
{
    public MusicInputMode InputMode { get; init; } = MusicInputMode.BackgroundPostMessage;

    public MusicPlaybackMode PlaybackMode { get; init; } = MusicPlaybackMode.Sequential;

    public double Speed { get; init; } = 1.0;

    public TimeSpan StartPosition { get; init; }
}
