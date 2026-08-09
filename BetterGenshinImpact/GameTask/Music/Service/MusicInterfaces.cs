using BetterGenshinImpact.GameTask.Music.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace BetterGenshinImpact.GameTask.Music.Service;

public interface IMusicScoreParser
{
    bool CanParse(string path);

    Task<PerformanceScore> ParseAsync(string path, string rootFolder, CancellationToken cancellationToken);
}

public interface IMusicLibraryService : IDisposable
{
    event EventHandler? FilesChanged;

    event EventHandler<MusicScoreParseFailedEventArgs>? ScoreParseFailed;

    Task<IReadOnlyList<PerformanceScore>> ScanAsync(string rootFolder, CancellationToken cancellationToken);

    void Watch(string rootFolder);
}

public sealed class MusicScoreParseFailedEventArgs(string filePath, string errorMessage) : EventArgs
{
    public string FilePath { get; } = filePath;

    public string ErrorMessage { get; } = errorMessage;
}

public interface IMusicCoverService
{
    Task<ImageSource?> GetCoverAsync(string songName, CancellationToken cancellationToken);
}

public interface IInstrumentProfileService
{
    ObservableCollection<InstrumentProfile> Profiles { get; }

    InstrumentProfile StandardProfile { get; }

    InstrumentProfile Find(string? name);

    void Save();
}

public interface IMusicTimelineBuilder
{
    PerformanceTimeline Build(
        PerformanceScore score,
        InstrumentProfile outputProfile,
        int transpose);
}

public interface IKeyInputTransport
{
    MusicInputMode Mode { get; }

    void KeyDown(char key);

    void KeyUp(char key);

    void ReleaseAll();
}

public interface IMusicPlaybackService
{
    event EventHandler<PlaybackSnapshot>? SnapshotChanged;

    PlaybackSnapshot Snapshot { get; }

    Task RunPlaylistAsync(
        IReadOnlyList<PerformanceScore> queue,
        int startIndex,
        MusicPlaybackOptions options,
        CancellationToken cancellationToken);

    void Pause();

    void Resume();

    void Stop();

    void Next();

    void Previous();

    void Seek(TimeSpan position);

    void SetSpeed(double speed);

    void SetPlaybackMode(MusicPlaybackMode mode);
}

public interface IMusicStateStore
{
    MusicLibraryState State { get; }

    void Save();
}
