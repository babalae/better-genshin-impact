using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.Music.Model;

public sealed class MusicLibraryState
{
    public List<string> MusicFolderHistory { get; set; } = [];

    public Dictionary<string, MusicItemPreference> Items { get; set; } =
        new(System.StringComparer.OrdinalIgnoreCase);

    public string CurrentTrackFullPath { get; set; } = string.Empty;

    public double CurrentPositionMilliseconds { get; set; }
}

public sealed class MusicItemPreference
{
    public string OutputProfileName { get; set; } = string.Empty;

    public int Transpose { get; set; }

    public List<int> DisabledTrackIndexes { get; set; } = [];
}
